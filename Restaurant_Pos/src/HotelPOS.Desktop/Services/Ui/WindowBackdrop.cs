using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HotelPOS.Desktop.Services.Ui;

// Windows 11 window frame: a dark title bar in the dark theme and the Mica backdrop (22H2 and later). The window
// content keeps its own solid background, so this changes the frame only; on Windows 10 the calls are ignored.
internal static class WindowBackdrop
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmSystemBackdropType = 38;
    private const int BackdropMica = 2;
    private const int FirstBuildWithBackdropType = 22621;

    // Returns false when the window has no handle yet (not shown); the caller retries on SourceInitialized.
    public static bool Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var darkMode = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int));
            if (System.Environment.OSVersion.Version.Build >= FirstBuildWithBackdropType)
            {
                var backdrop = BackdropMica;
                _ = DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref backdrop, sizeof(int));
            }
        }
        catch (DllNotFoundException)
        {
            // No desktop window manager (e.g. Server Core): keep the standard frame.
        }

        return true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
