using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Desktop.Controls;
using HotelPOS.Desktop.Shell;

namespace HotelPOS.Desktop.Tests.Rendering;

// Screen-recorded demo (HOTELPOS_DEMO_SCREEN=1 together with HOTELPOS_DEMO_RECORD): the chapter runs in a real window
// on the primary screen and the steps act like a person. Every command the chapter runs and every value it sets goes
// through H: when a visible control is bound to that command or property, the real mouse moves there and clicks or
// types (SendInput); otherwise the command runs or the property is set directly, as before. ffmpeg records the window
// region from the screen with the cursor (gdigrab). Without HOTELPOS_DEMO_SCREEN, H simply runs commands and sets
// properties, so the same chapter code drives the screenshot pack and the off-screen recording.
public partial class LiveFlowScreenshots
{
    private static readonly bool ScreenMode = System.Environment.GetEnvironmentVariable("HOTELPOS_DEMO_SCREEN") == "1";

    private static Human H { get; } = new();

    private interface IDemoRecorder : IAsyncDisposable
    {
        double Now { get; }
    }

    // ---------------------------------------------------------------- the person at the terminal

    private sealed class Human
    {
        private const int MoveSteps = 22;

        public Window? Window { get; set; }

        public Action<string>? Log { get; set; }

        private void Trace(string message) => Log?.Invoke("human: " + message);

        private bool Live => ScreenMode && Window is { IsVisible: true };

        // Runs a command the way the chapter would, by clicking its button when one is on screen.
        public async Task RunAsync(ICommand command, object? parameter = null)
        {
            var button = Live ? FindCommandButton(command, parameter) : null;
            Trace($"run {command.GetType().Name} live={Live} button={button?.GetType().Name ?? "none"} visuals={Visuals().Count()}");
            // The click is processed while ClickAsync still waits, so the execution task to compare against is read first.
            var before = (command as IAsyncRelayCommand)?.ExecutionTask;
            if (button is not null && await ClickAsync(button))
            {
                if (command is IAsyncRelayCommand async)
                {
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                    while (ReferenceEquals(async.ExecutionTask, before) && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(50);
                    }

                    if (!ReferenceEquals(async.ExecutionTask, before) && async.ExecutionTask is { } task)
                    {
                        try
                        {
                            await task;
                        }
                        catch
                        {
                            // The command reports its own failure through the view-model; the step's checks see it.
                        }
                    }
                    else if (command.CanExecute(parameter))
                    {
                        await async.ExecuteAsync(parameter);
                    }
                }
                else
                {
                    await Task.Delay(250);
                }

                return;
            }

            if (command is IAsyncRelayCommand asyncCommand)
            {
                await asyncCommand.ExecuteAsync(parameter);
            }
            else
            {
                command.Execute(parameter);
            }
        }

        // Sets a view-model property the way the chapter would: typing into its box, ticking its box or picking its
        // row when one is on screen, otherwise through the property setter.
        public async Task SetAsync(object target, string property, object? value)
        {
            if (Live)
            {
                foreach (var element in Visuals().OfType<FrameworkElement>().Where(e => e.IsVisible))
                {
                    switch (element)
                    {
                        case PasswordBox box when BoundTo(box, PasswordBoxAssistant.BoundPasswordProperty, target, property):
                            if (await ClickAsync(box))
                            {
                                await TypeAsync(value?.ToString() ?? "", clear: box.Password.Length > 0);
                                return;
                            }

                            break;
                        case TextBox box when BoundTo(box, TextBox.TextProperty, target, property):
                            if (await ClickAsync(box))
                            {
                                await TypeAsync(value?.ToString() ?? "", clear: box.Text.Length > 0);
                                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                                await Task.Delay(150);
                                return;
                            }

                            break;
                        case ToggleButton toggle when BoundTo(toggle, ToggleButton.IsCheckedProperty, target, property, value):
                            var wanted = value is bool b ? b : true;
                            if (toggle.IsChecked == wanted)
                            {
                                return;
                            }

                            if (await ClickAsync(toggle))
                            {
                                await Task.Delay(200);
                                return;
                            }

                            break;
                        case ComboBox combo when BoundTo(combo, Selector.SelectedItemProperty, target, property) && value is not null && combo.Items.Contains(value):
                            if (await ClickAsync(combo))
                            {
                                await Task.Delay(350);
                                if (combo.ItemContainerGenerator.ContainerFromItem(value) is FrameworkElement option && await ClickAsync(option))
                                {
                                    await Task.Delay(200);
                                    return;
                                }

                                combo.IsDropDownOpen = false;
                            }

                            break;
                        case Selector selector when BoundTo(selector, Selector.SelectedItemProperty, target, property) && value is not null && selector.Items.Contains(value):
                            if (await ClickItemAsync(selector, value))
                            {
                                return;
                            }

                            break;
                    }
                }
            }

            Trace($"set {target.GetType().Name}.{property} live={Live}: no control, property set directly");
            SetProperty(target, property, value);
        }

        // Clicks the row, card or tab that shows this item, wherever it is listed.
        public async Task<bool> ClickItemAsync(object item)
        {
            if (!Live)
            {
                return false;
            }

            foreach (var selector in Visuals().OfType<Selector>().Where(s => s.IsVisible && s.Items.Contains(item)))
            {
                if (await ClickItemAsync(selector, item))
                {
                    return true;
                }
            }

            var card = Visuals().OfType<FrameworkElement>().FirstOrDefault(e => e.IsVisible && ReferenceEquals(e.DataContext, item) && e is ButtonBase or ContentControl { Parent: ItemsControl });
            return card is not null && await ClickAsync(card);
        }

        public async Task<bool> NavAsync(ShellViewModel shell, string module)
        {
            if (!Live)
            {
                return false;
            }

            var item = shell.NavItems.FirstOrDefault(n => n.Key == module);
            var list = Visuals().OfType<ListBox>().FirstOrDefault(l => ReferenceEquals(l.ItemsSource, shell.NavItems));
            Trace($"nav {module}: item={(item is null ? "none" : "ok")} list={(list is null ? "none" : "ok")} listboxes={Visuals().OfType<ListBox>().Count()}");
            if (item is null || list is null)
            {
                return false;
            }

            if (!await ClickItemAsync(list, item))
            {
                return false;
            }

            await Task.Delay(300);
            return shell.SelectedNavItem?.Key == module;
        }

        // ---------------------------------------------------------------- finding controls

        private IEnumerable<DependencyObject> Visuals()
        {
            if (Window is null)
            {
                yield break;
            }

            var stack = new Stack<DependencyObject>();
            stack.Push(Window);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                yield return current;
                var count = VisualTreeHelper.GetChildrenCount(current);
                for (var i = count - 1; i >= 0; i--)
                {
                    stack.Push(VisualTreeHelper.GetChild(current, i));
                }

                // Popups (combo drop-downs) are separate visual trees.
                if (current is Popup { IsOpen: true, Child: { } child })
                {
                    stack.Push(child);
                }
            }
        }

        private FrameworkElement? FindCommandButton(ICommand command, object? parameter)
        {
            var candidates = Visuals().OfType<ButtonBase>().Where(b => b.IsVisible && b.IsEnabled && ReferenceEquals(b.Command, command)).ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            if (parameter is null)
            {
                return candidates.FirstOrDefault(b => b.CommandParameter is null) ?? candidates[0];
            }

            return candidates.FirstOrDefault(b => Equals(b.CommandParameter, parameter) || string.Equals(b.CommandParameter?.ToString(), parameter.ToString(), StringComparison.Ordinal));
        }

        private static bool BoundTo(DependencyObject element, DependencyProperty dp, object target, string property, object? value = null)
        {
            if (BindingOperations.GetBindingExpression(element, dp) is not { } expression)
            {
                return false;
            }

            if (!ReferenceEquals(expression.ResolvedSource, target) || expression.ResolvedSourcePropertyName != property)
            {
                return false;
            }

            // Radio-style toggles bound through a converter parameter (ViewMode = "Map"): only the one for this value.
            var converterParameter = expression.ParentBinding.ConverterParameter;
            return converterParameter is null || value is null || string.Equals(converterParameter.ToString(), value.ToString(), StringComparison.Ordinal);
        }

        private async Task<bool> ClickItemAsync(Selector selector, object item)
        {
            if (selector is ListBox listBox)
            {
                listBox.ScrollIntoView(item);
            }
            else if (selector is DataGrid grid)
            {
                grid.ScrollIntoView(item);
            }

            selector.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (selector.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container)
            {
                return false;
            }

            if (!await ClickAsync(container))
            {
                return false;
            }

            await Task.Delay(250);
            return true;
        }

        private static void SetProperty(object target, string property, object? value)
        {
            var info = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
                       ?? throw new InvalidOperationException($"{target.GetType().Name} has no property {property}");
            info.SetValue(target, value);
        }

        // ---------------------------------------------------------------- the mouse and the keyboard

        private async Task<bool> ClickAsync(FrameworkElement element)
        {
            if (!TryScreenPoint(element, out var point))
            {
                element.BringIntoView();
                element.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Background);
                await Task.Delay(150);
                if (!TryScreenPoint(element, out point))
                {
                    return false;
                }
            }

            await MoveAsync(point);
            SendMouse(Mouse.LeftDown);
            await Task.Delay(70);
            SendMouse(Mouse.LeftUp);
            await Task.Delay(120);
            return true;
        }

        private bool TryScreenPoint(FrameworkElement element, out Point point)
        {
            point = default;
            if (Window is null || !element.IsVisible || element.ActualWidth < 1 || element.ActualHeight < 1)
            {
                return false;
            }

            var centre = new Point(element.ActualWidth / 2, element.ActualHeight / 2);
            var inWindow = element.TranslatePoint(centre, Window);
            if (inWindow.X < 0 || inWindow.Y < 0 || inWindow.X > Window.ActualWidth || inWindow.Y > Window.ActualHeight)
            {
                return false;
            }

            // Elements inside a popup (a combo drop-down) live in their own window: no hit test against the main one.
            if (!ReferenceEquals(PresentationSource.FromVisual(element), PresentationSource.FromVisual(Window)))
            {
                point = element.PointToScreen(centre);
                return true;
            }

            // The point must really reach this element the way a mouse click would (InputHitTest honours
            // IsHitTestVisible and visibility; a dialog overlay or a scrolled-away row fails here).
            var hit = Window.InputHitTest(inWindow) as DependencyObject;
            Trace($"hit-test {element.GetType().Name} at {inWindow.X:0},{inWindow.Y:0} -> {hit?.GetType().Name ?? "nothing"}");
            for (var d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, element))
                {
                    point = element.PointToScreen(centre);
                    return true;
                }
            }

            return false;
        }

        private static async Task MoveAsync(Point to)
        {
            GetCursorPos(out var from);
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var steps = Math.Max(6, (int)Math.Min(MoveSteps, distance / 25));
            for (var i = 1; i <= steps; i++)
            {
                var t = i / (double)steps;
                var ease = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                SendMouseMove(from.X + dx * ease, from.Y + dy * ease);
                await Task.Delay(16);
            }

            SendMouseMove(to.X, to.Y);
            await Task.Delay(90);
        }

        private static async Task TypeAsync(string text, bool clear)
        {
            if (clear)
            {
                SendKey(0x11, true);
                SendKey(0x41, true);
                SendKey(0x41, false);
                SendKey(0x11, false);
                await Task.Delay(60);
            }

            foreach (var ch in text)
            {
                SendChar(ch);
                await Task.Delay(ch == ' ' ? 60 : 38);
            }

            await Task.Delay(120);
        }

        private static void SendMouseMove(double x, double y)
        {
            var width = GetSystemMetrics(78);
            var height = GetSystemMetrics(79);
            var left = GetSystemMetrics(76);
            var top = GetSystemMetrics(77);
            var input = new INPUT { type = 0 };
            input.u.mi.dx = (int)Math.Round((x - left) * 65535.0 / width);
            input.u.mi.dy = (int)Math.Round((y - top) * 65535.0 / height);
            input.u.mi.dwFlags = 0x0001u | 0x8000u | 0x4000u; // MOVE | ABSOLUTE | VIRTUALDESK
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static void SendMouse(uint flags)
        {
            var input = new INPUT { type = 0 };
            input.u.mi.dwFlags = flags;
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static void SendKey(ushort vk, bool down)
        {
            var input = new INPUT { type = 1 };
            input.u.ki.wVk = vk;
            input.u.ki.dwFlags = down ? 0u : 0x0002u;
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static void SendChar(char ch)
        {
            var down = new INPUT { type = 1 };
            down.u.ki.wScan = ch;
            down.u.ki.dwFlags = 0x0004u;
            var up = down;
            up.u.ki.dwFlags = 0x0004u | 0x0002u;
            SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
        }

        private static class Mouse
        {
            public const uint LeftDown = 0x0002;
            public const uint LeftUp = 0x0004;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private static void GetCursorPos(out Point point)
        {
            GetCursorPosNative(out var p);
            point = new Point(p.X, p.Y);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
        private static extern bool GetCursorPosNative(out POINT point);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);
    }

    // ---------------------------------------------------------------- the screen recorder

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    private static void BecomeDpiAware()
    {
        // Per-monitor V2 (-4): the window is laid out at the monitor's scale and PointToScreen returns real pixels.
        SetProcessDpiAwarenessContext(new IntPtr(-4));
    }

    private sealed class ScreenRecorder : IDemoRecorder
    {
        private readonly Window _window;
        private readonly Process _ffmpeg;
        private readonly Stopwatch _clock = new();
        private readonly TaskCompletionSource _started = new();

        public ScreenRecorder(Terminal terminal, string mp4)
        {
            var template = new MainWindow { DataContext = terminal.Main };
            var root = (FrameworkElement)template.Content;
            template.Content = null;
            template.Close();
            _window = new Window
            {
                Title = "HotelPOS demo",
                Content = root,
                DataContext = terminal.Main,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Left = 0,
                Top = 0,
                Width = 1368,
                Height = 800,
                Topmost = true,
                ShowInTaskbar = false,
            };
            _window.SetResourceReference(Control.BackgroundProperty, "Brush.Background");
            _window.SetResourceReference(TextElement.FontSizeProperty, "Font.Size.Body");
            _window.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextPrimary");
            TextElement.SetFontFamily(_window, (FontFamily)Application.Current.Resources["Font.Body"]);
            _window.Show();
            _window.Activate();
            _window.UpdateLayout();
            H.Window = _window;

            var scale = PresentationSource.FromVisual(_window)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            var width = (int)Math.Round(_window.Width * scale) / 2 * 2;
            var height = (int)Math.Round(_window.Height * scale) / 2 * 2;
            var ffmpegPath = System.Environment.GetEnvironmentVariable("HOTELPOS_FFMPEG") ?? "ffmpeg";
            var arguments = $"-y -loglevel info -f gdigrab -framerate 15 -draw_mouse 1 -offset_x 0 -offset_y 0 -video_size {width}x{height} -i desktop " +
                            $"-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart \"{mp4}\"";
            _ffmpeg = Process.Start(new ProcessStartInfo(ffmpegPath, arguments)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("ffmpeg did not start");
            _ffmpeg.ErrorDataReceived += (_, e) =>
            {
                // "Output #0" is printed once the first screen frame has been read: the clock starts there.
                if (e.Data is not null && e.Data.StartsWith("Output #0", StringComparison.Ordinal) && !_clock.IsRunning)
                {
                    _clock.Start();
                    _started.TrySetResult();
                }
            };
            _ffmpeg.BeginErrorReadLine();
            if (!_started.Task.Wait(TimeSpan.FromSeconds(8)))
            {
                _clock.Start();
            }
        }

        public double Now => _clock.Elapsed.TotalSeconds;

        public async ValueTask DisposeAsync()
        {
            try
            {
                _ffmpeg.StandardInput.Write('q');
                _ffmpeg.StandardInput.Flush();
            }
            catch
            {
                // ffmpeg may already be gone.
            }

            await _ffmpeg.WaitForExitAsync();
            H.Window = null;
            _window.Close();
        }
    }
}
