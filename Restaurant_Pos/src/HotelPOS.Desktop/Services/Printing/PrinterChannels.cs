using System.IO;
using System.Net.Sockets;
using System.Printing;
using System.Runtime.InteropServices;
using System.Windows.Documents;
using System.Windows.Xps;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Desktop.Services.Printing;

public interface IEscPosChannel
{
    Task SendAsync(byte[] data, PrinterProfile profile, CancellationToken cancellationToken);
}

public interface IWindowsPrinterChannel
{
    Task PrintAsync(object document, PrinterProfile profile, int copies, CancellationToken cancellationToken);

    IReadOnlyList<string> InstalledPrinters();
}

public sealed class NetworkPrinterChannel : IEscPosChannel
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public async Task SendAsync(byte[] data, PrinterProfile profile, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            await client.ConnectAsync(profile.Host, profile.Port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync(data, timeout.Token);
            await stream.FlushAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new PrintException($"Printer {profile.Host}:{profile.Port} did not answer. Check that it is on and connected to the network.", ex);
        }
    }
}

// Sends raw bytes through the Windows spooler ("Generic / Text Only" driver), so USB thermal printers need no port name.
public sealed class RawPrinterChannel : IEscPosChannel
{
    public Task SendAsync(byte[] data, PrinterProfile profile, CancellationToken cancellationToken) =>
        Task.Run(() => Send(data, profile.PrinterName), cancellationToken);

    private static void Send(byte[] data, string printerName)
    {
        if (!NativeMethods.OpenPrinter(printerName, out var printer, IntPtr.Zero))
        {
            throw new PrintException($"Printer \"{printerName}\" was not found on this PC.", new System.ComponentModel.Win32Exception());
        }

        try
        {
            var info = new NativeMethods.DocInfo { DocName = "HotelPOS", DataType = "RAW" };
            if (NativeMethods.StartDocPrinter(printer, 1, ref info) == 0)
            {
                throw new PrintException($"Printer \"{printerName}\" refused the document.", new System.ComponentModel.Win32Exception());
            }

            try
            {
                NativeMethods.StartPagePrinter(printer);
                var buffer = Marshal.AllocHGlobal(data.Length);
                try
                {
                    Marshal.Copy(data, 0, buffer, data.Length);
                    if (!NativeMethods.WritePrinter(printer, buffer, data.Length, out var written) || written != data.Length)
                    {
                        throw new PrintException($"Printer \"{printerName}\" did not accept all the data.", new System.ComponentModel.Win32Exception());
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                    NativeMethods.EndPagePrinter(printer);
                }
            }
            finally
            {
                NativeMethods.EndDocPrinter(printer);
            }
        }
        finally
        {
            NativeMethods.ClosePrinter(printer);
        }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DocInfo
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string DocName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? OutputFile;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string DataType;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

        [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool ClosePrinter(IntPtr printer);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int StartDocPrinter(IntPtr printer, int level, ref DocInfo docInfo);

        [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool EndDocPrinter(IntPtr printer);

        [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool StartPagePrinter(IntPtr printer);

        [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool EndPagePrinter(IntPtr printer);

        [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool WritePrinter(IntPtr printer, IntPtr bytes, int count, out int written);
    }
}

public sealed class WindowsPrinterChannel : IWindowsPrinterChannel
{
    private readonly FlowDocumentRenderer _renderer;

    public WindowsPrinterChannel(FlowDocumentRenderer renderer)
    {
        _renderer = renderer;
    }

    // WPF printing needs an STA thread; a private one keeps the UI free and works from the background queue.
    public Task PrintAsync(object document, PrinterProfile profile, int copies, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Print(document, profile, copies);
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex is PrintException ? ex : new PrintException($"Windows printer \"{profile.PrinterName}\" failed: {ex.Message}", ex));
            }
        })
        {
            IsBackground = true,
            Name = "HotelPOS print",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(cancellationToken);
    }

    public IReadOnlyList<string> InstalledPrinters()
    {
        try
        {
            using var server = new LocalPrintServer();
            return server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                .Select(q => q.FullName)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is PrintSystemException or InvalidOperationException)
        {
            return Array.Empty<string>();
        }
    }

    private void Print(object document, PrinterProfile profile, int copies)
    {
        using var server = new LocalPrintServer();
        using var queue = string.IsNullOrWhiteSpace(profile.PrinterName)
            ? LocalPrintServer.GetDefaultPrintQueue()
            : server.GetPrintQueue(profile.PrinterName);
        var ticket = queue.DefaultPrintTicket ?? new PrintTicket();
        ticket.CopyCount = Math.Clamp(copies, 1, PrintLimits.MaxCopies);
        var flow = _renderer.Render(document, profile);
        var writer = System.Printing.PrintQueue.CreateXpsDocumentWriter(queue);
        writer.Write(((IDocumentPaginatorSource)flow).DocumentPaginator, ticket);
    }
}
