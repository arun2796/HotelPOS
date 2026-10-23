using Android.Content;
using Android.OS;
using Android.Print;
using HotelPOS.Client.Services;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Android.Services;

/// <summary>
/// Print KOT on the phone or tablet: the web view cannot open a blob in a new tab (window.open does nothing inside the
/// app), so the PDF goes to the app's cache and from there to Android's print dialog, which lists the printers the
/// device knows (Wi-Fi printers with a print service, "Save as PDF", or a share to another app). When no print
/// service is available the PDF is opened with the device's viewer instead.
/// </summary>
public sealed class MauiPdfOpener : IPdfOpener
{
    private readonly ILogger<MauiPdfOpener> _logger;

    public MauiPdfOpener(ILogger<MauiPdfOpener> logger)
    {
        _logger = logger;
    }

    public async Task<string?> OpenAsync(byte[] pdf, string fileName, CancellationToken ct = default)
    {
        var folder = Path.Combine(FileSystem.CacheDirectory, "kot");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        await File.WriteAllBytesAsync(path, pdf, ct);

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No current activity.");
                var manager = activity.GetSystemService(Context.PrintService) as PrintManager
                    ?? throw new InvalidOperationException("No print service on this device.");
                manager.Print(fileName, new PdfFileAdapter(path, fileName), new PrintAttributes.Builder().Build());
            });
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Java.Lang.Exception)
        {
            _logger.LogWarning(ex, "Android print dialog unavailable; opening the KOT with the device's viewer instead");
        }

        try
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest(fileName, new ReadOnlyFile(path, "application/pdf")));
            return null;
        }
        catch (Exception ex) when (ex is not System.OperationCanceledException)
        {
            _logger.LogError(ex, "The KOT PDF could not be opened on this device");
            return "This device has no app that can print or open a PDF.";
        }
    }

    /// <summary>Hands an already rendered PDF file to the print framework page by page as it is.</summary>
    private sealed class PdfFileAdapter : PrintDocumentAdapter
    {
        private readonly string _path;
        private readonly string _name;

        public PdfFileAdapter(string path, string name)
        {
            _path = path;
            _name = name;
        }

        public override void OnLayout(PrintAttributes? oldAttributes, PrintAttributes? newAttributes, CancellationSignal? cancellationSignal, LayoutResultCallback? callback, Bundle? extras)
        {
            if (cancellationSignal?.IsCanceled == true)
            {
                callback?.OnLayoutCancelled();
                return;
            }

            var info = new PrintDocumentInfo.Builder(_name).SetContentType(PrintContentType.Document).Build();
            callback?.OnLayoutFinished(info, true);
        }

        public override void OnWrite(PageRange[]? pages, ParcelFileDescriptor? destination, CancellationSignal? cancellationSignal, WriteResultCallback? callback)
        {
            try
            {
                if (destination is null)
                {
                    callback?.OnWriteFailed("No destination.");
                    return;
                }

                using var input = File.OpenRead(_path);
                using var output = new Java.IO.FileOutputStream(destination.FileDescriptor);
                var buffer = new byte[16 * 1024];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (cancellationSignal?.IsCanceled == true)
                    {
                        callback?.OnWriteCancelled();
                        return;
                    }

                    output.Write(buffer, 0, read);
                }

                callback?.OnWriteFinished(new[] { PageRange.AllPages! });
            }
            catch (Exception ex) when (ex is IOException or Java.IO.IOException)
            {
                callback?.OnWriteFailed(ex.Message);
            }
        }
    }
}
