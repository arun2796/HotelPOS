using System.Windows;
using System.Windows.Input;

namespace HotelPOS.Desktop.Shell;

public partial class MainWindow : Window
{
    private WindowState _restoreState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // F11 toggles kiosk-style full screen for kitchen displays and waiter tablets (a view concern only).
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F11)
        {
            return;
        }

        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _restoreState;
        }
        else
        {
            _restoreState = WindowState;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }

        e.Handled = true;
    }
}
