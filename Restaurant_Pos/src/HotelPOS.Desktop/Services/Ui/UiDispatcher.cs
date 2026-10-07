using System.Windows;

namespace HotelPOS.Desktop.Services.Ui;

/// <summary>Marshals work to the UI thread. Abstracted so view-models can be unit tested.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

public sealed class WpfUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }
}
