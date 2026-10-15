using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Realtime;

namespace HotelPOS.Desktop.Shell;

public sealed partial class StatusBarViewModel : ObservableObject, IDisposable
{
    private readonly IRealtimeClient _realtime;
    private readonly IClientSettingsService _settings;
    private readonly IPrintQueue _printQueue;

    public StatusBarViewModel(IRealtimeClient realtime, IClientSettingsService settings, IPrintQueue printQueue)
    {
        _realtime = realtime;
        _settings = settings;
        _printQueue = printQueue;
        _status = realtime.Status;
        _realtime.StatusChanged += OnStatusChanged;
        _printQueue.Changed += OnPrintQueueChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsOnline))]
    private ConnectionStatus _status;

    [ObservableProperty]
    private DateTime? _lastConnectedAt;

    public string StatusText => Status switch
    {
        ConnectionStatus.Connected => "Connected",
        ConnectionStatus.Connecting => "Connecting…",
        ConnectionStatus.Reconnecting => "Reconnecting…",
        _ => "Disconnected",
    };

    public bool IsOnline => Status == ConnectionStatus.Connected;

    public string ServerAddress => _settings.Current.ApiBaseUrl;

    public string DeviceName => _settings.Current.DeviceName;

    public string Version => "v" + AppInfo.Version;

    public int FailedPrintCount => _printQueue.FailedCount;

    public bool HasFailedPrints => FailedPrintCount > 0;

    public string FailedPrintText => FailedPrintCount == 1 ? "1 print failed" : $"{FailedPrintCount} prints failed";

    public void Dispose()
    {
        _realtime.StatusChanged -= OnStatusChanged;
        _printQueue.Changed -= OnPrintQueueChanged;
    }

    [RelayCommand]
    private void RetryPrints() => _printQueue.RetryAll();

    [RelayCommand]
    private void DiscardPrints() => _printQueue.DiscardAll();

    private void OnStatusChanged(object? sender, ConnectionStatus status)
    {
        Status = status;
        if (status == ConnectionStatus.Connected)
        {
            LastConnectedAt = DateTime.Now;
        }
    }

    private void OnPrintQueueChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(FailedPrintCount));
        OnPropertyChanged(nameof(HasFailedPrints));
        OnPropertyChanged(nameof(FailedPrintText));
    }
}
