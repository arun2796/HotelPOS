using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

public sealed record BackupRow(BackupDto Backup)
{
    public string Started => Backup.StartedAtUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture);

    public string Duration => Backup.FinishedAtUtc is { } finished
        ? $"{Math.Max(1, (finished - Backup.StartedAtUtc).TotalSeconds):N0} s"
        : "running";

    public string Size => Backup.SizeBytes == 0 ? "—" : $"{Backup.SizeBytes / 1024d / 1024d:N1} MB";

    public string Status => Backup.Status switch
    {
        BackupStatus.Succeeded => "OK",
        BackupStatus.Failed => "FAILED",
        _ => "Running",
    };

    public bool IsFailed => Backup.Status == BackupStatus.Failed;

    public string Trigger => Backup.Trigger == BackupTrigger.Schedule ? "Scheduled" : $"Manual ({Backup.TriggeredBy})";
}

public sealed partial class BackupViewModel : ViewModelBase, INavigationAware, IRefreshable
{
    private readonly IOperationsApi _operations;
    private readonly IAdminSettingsApi _settings;
    private readonly INotificationService _notifications;

    public BackupViewModel(IOperationsApi operations, IAdminSettingsApi settings, INotificationService notifications)
    {
        _operations = operations;
        _settings = settings;
        _notifications = notifications;
    }

    public ObservableCollection<BackupRow> Backups { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailure))]
    private string? _failureText;

    [ObservableProperty]
    private string _folder = string.Empty;

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _time = "03:30";

    [ObservableProperty]
    private string _retentionDays = "14";

    public bool HasFailure => FailureText is not null;

    public Task OnNavigatedToAsync(object? parameter) => LoadAsync();

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    public Task RefreshAsync() => LoadAsync();

    protected override void OnBusyChanged()
    {
        BackupNowCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task BackupNowAsync() => ExecuteAsync(async () =>
    {
        var result = await _operations.BackupNowAsync();
        if (result.Success && result.Data is not null)
        {
            _notifications.Success($"Backup {result.Data.FileName} written and verified.");
        }
        else
        {
            _notifications.Error(result.Message, result.CorrelationId);
        }

        await LoadAsync();
    });

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task SaveSettingsAsync()
    {
        if (!TimeOnly.TryParseExact(Time.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            ErrorMessage = "Backup time must be HH:mm, for example 03:30.";
            return;
        }

        if (!int.TryParse(RetentionDays.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days < 1)
        {
            ErrorMessage = "Keep backups for at least 1 day.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _settings.UpdateAsync(new UpdateSettingsRequest
            {
                Items = new[]
                {
                    new SettingValue { Key = SettingKeys.BackupEnabled, Value = Enabled ? "true" : "false" },
                    new SettingValue { Key = SettingKeys.BackupTime, Value = Time.Trim() },
                    new SettingValue { Key = SettingKeys.BackupRetentionDays, Value = days.ToString(CultureInfo.InvariantCulture) },
                    new SettingValue { Key = SettingKeys.BackupPath, Value = Folder.Trim() },
                },
            });
            if (result.Success)
            {
                _notifications.Success("Backup settings saved.");
                await LoadAsync();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        });
    }

    private async Task LoadAsync()
    {
        ErrorMessage = null;
        var overview = await FetchAsync(_operations.GetBackupsAsync());
        if (overview is null)
        {
            return;
        }

        Folder = overview.Folder;
        Enabled = overview.Enabled;
        Time = overview.Time;
        RetentionDays = overview.RetentionDays.ToString(CultureInfo.InvariantCulture);
        FailureText = overview.LastFailure is { } failure
            ? $"The last backup failed ({new BackupRow(failure).Started}): {failure.Error}"
            : null;

        Backups.Clear();
        foreach (var backup in overview.Backups)
        {
            Backups.Add(new BackupRow(backup));
        }
    }
}
