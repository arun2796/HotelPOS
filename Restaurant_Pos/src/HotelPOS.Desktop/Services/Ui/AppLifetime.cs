namespace HotelPOS.Desktop.Services.Ui;

// Ends the application (after an update was handed to the installer).
public interface IAppLifetime
{
    void Exit();
}

public sealed class WpfAppLifetime : IAppLifetime
{
    public void Exit() => System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
}
