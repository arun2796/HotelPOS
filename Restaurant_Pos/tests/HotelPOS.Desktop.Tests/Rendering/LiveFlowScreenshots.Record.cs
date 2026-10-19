using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HotelPOS.Desktop.Shell;

namespace HotelPOS.Desktop.Tests.Rendering;

// Video recording for the demo pack (roles = "demo"). With HOTELPOS_DEMO_RECORD=<folder> every chapter is also recorded
// as <folder>\<chapter folder>.mp4: the live main view (the same visual RenderAsync captures, kept alive for the whole
// chapter) is rendered ten times a second and piped to ffmpeg (HOTELPOS_FFMPEG or "ffmpeg" on PATH). After each step the
// run pauses for the step's narration (HOTELPOS_DEMO_NARRATION = JSON { "<chapter folder>/<step slug>": seconds }) so
// the result stays on screen while the voice explains it; <chapter folder>.json lists every step with its start and end
// second in the video. docs/ui-ux/tools/demo-video-live.mjs cuts the videos by those marks.
public partial class LiveFlowScreenshots
{
    private static readonly string? RecordDirectory = System.Environment.GetEnvironmentVariable("HOTELPOS_DEMO_RECORD");
    private static readonly Dictionary<string, double> NarrationSeconds = LoadNarrationSeconds();

    private DemoRecorder? _recorder;
    private readonly List<StepMark> _marks = new();

    private sealed record StepMark(int N, string Chapter, string Step, string Key, double Start, double End, string Result, bool Included);

    private static Dictionary<string, double> LoadNarrationSeconds()
    {
        var file = System.Environment.GetEnvironmentVariable("HOTELPOS_DEMO_NARRATION");
        var map = new Dictionary<string, double>();
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            return map;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(file));
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number)
            {
                map[property.Name] = property.Value.GetDouble();
            }
        }

        return map;
    }

    // Same rule as the JS side: lower case, every run of non-alphanumerics becomes one dash.
    private static string StepKey(string folder, string step) =>
        folder + "/" + Regex.Replace(step.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    private void EnsureRecording()
    {
        if (RecordDirectory is null || _demoTerminal is null || _recorder is not null)
        {
            return;
        }

        Directory.CreateDirectory(RecordDirectory);
        _recorder = new DemoRecorder(_demoTerminal, Path.Combine(RecordDirectory, _folder + ".mp4"));
        Note("recording " + _folder + ".mp4");
    }

    private async Task MarkStepAsync(int n, string step, string result, double start)
    {
        if (_recorder is null)
        {
            return;
        }

        var key = StepKey(_folder, step);
        var included = NarrationSeconds.TryGetValue(key, out var seconds);
        if (included)
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds + 0.8));
        }

        _marks.Add(new StepMark(n, _demoChapter, step, key, start, _recorder.Now, result, included));
    }

    private async Task StopRecordingAsync()
    {
        if (_recorder is null)
        {
            return;
        }

        var recorder = _recorder;
        _recorder = null;
        await recorder.DisposeAsync();
        var json = JsonSerializer.Serialize(
            new { video = _folder + ".mp4", fps = DemoRecorder.Fps, steps = _marks },
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(Path.Combine(RecordDirectory!, _folder + ".json"), json);
        Note($"recorded {_marks.Count} steps, {recorder.Now:0.0} s");
        _marks.Clear();
    }

    private sealed class DemoRecorder : IAsyncDisposable
    {
        public const int Fps = 10;

        private readonly Border _host;
        private readonly Process _ffmpeg;
        private readonly Stream _stdin;
        private readonly Thread _writer;
        private readonly BlockingCollection<byte[]> _queue = new(boundedCapacity: 40);
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly RenderTargetBitmap _bitmap = new(Width, Height, 96, 96, PixelFormats.Pbgra32);
        private long _written;
        private bool _busy;

        public DemoRecorder(Terminal terminal, string mp4)
        {
            var window = new MainWindow { DataContext = terminal.Main };
            var root = (FrameworkElement)window.Content;
            window.Content = null;
            _host = new Border { Child = root, DataContext = terminal.Main };
            _host.SetResourceReference(Border.BackgroundProperty, "Brush.Background");
            TextElement.SetFontFamily(_host, (FontFamily)Application.Current.Resources["Font.Body"]);
            _host.SetResourceReference(TextElement.FontSizeProperty, "Font.Size.Body");
            _host.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextPrimary");
            _host.Measure(new Size(Width, Height));
            _host.Arrange(new Rect(0, 0, Width, Height));
            window.Close();

            var ffmpegPath = System.Environment.GetEnvironmentVariable("HOTELPOS_FFMPEG") ?? "ffmpeg";
            var arguments = $"-y -loglevel error -f rawvideo -pix_fmt bgra -s {Width}x{Height} -r {Fps} -i pipe:0 " +
                            $"-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart \"{mp4}\"";
            _ffmpeg = Process.Start(new ProcessStartInfo(ffmpegPath, arguments)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("ffmpeg did not start");
            _stdin = _ffmpeg.StandardInput.BaseStream;
            _writer = new Thread(WriteFrames) { IsBackground = true, Name = "demo-recorder" };
            _writer.Start();
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / Fps), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
            _timer.Start();
        }

        public double Now => _clock.Elapsed.TotalSeconds;

        private void WriteFrames()
        {
            foreach (var frame in _queue.GetConsumingEnumerable())
            {
                _stdin.Write(frame, 0, frame.Length);
            }

            _stdin.Flush();
            _stdin.Close();
        }

        // Keeps the video on the wall clock: a tick that comes late writes the frame as many times as were missed.
        private void Tick()
        {
            if (_busy)
            {
                return;
            }

            var due = (long)(_clock.Elapsed.TotalSeconds * Fps);
            if (due <= _written)
            {
                return;
            }

            _busy = true;
            try
            {
                _host.UpdateLayout();
                _bitmap.Clear();
                _bitmap.Render(_host);
                var pixels = new byte[Width * Height * 4];
                _bitmap.CopyPixels(pixels, Width * 4, 0);
                for (var i = _written; i < due; i++)
                {
                    _queue.Add(pixels);
                }

                _written = due;
            }
            finally
            {
                _busy = false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _timer.Stop();
            Tick();
            _queue.CompleteAdding();
            await Task.Run(_writer.Join);
            await _ffmpeg.WaitForExitAsync();
            _host.Child = null;
        }
    }
}
