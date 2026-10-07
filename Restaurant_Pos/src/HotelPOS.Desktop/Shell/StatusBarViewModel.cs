using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Realtime;

namespace HotelPOS.Desktop.Shell;

/// <summary>Bottom bar: live connection indicator (green / amber / red), server, terminal and version.</summary>
public sealed partial class StatusBarViewModel : ObservableObject, IDisposable
{
    private readonly IRealtimeClient _realtime;
    private readonly IClientSettingsService _settings;

    public StatusBarViewModel(IRealtimeClient realtime, IClientSettingsService settings)
    {
        _realtime = realtime;
        _settings = settings;
        _status = realtime.Status;
        _realtime.StatusChanged += OnStatusChanged;
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

    public void Dispose() => _realtime.StatusChanged -= OnStatusChanged;

    private void OnStatusChanged(object? sender, ConnectionStatus status)
    {
        Status = status;
        if (status == ConnectionStatus.Connected)
        {
            LastConnectedAt = DateTime.Now;
        }
    }
}
