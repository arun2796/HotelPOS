using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

public sealed record LevelOption(string? Code, string Name);

// What a technician needs without a remote-desktop session on the server: the API log and a broadcast message.
public sealed partial class ServerViewModel : ObservableObject, INavigationAware, IRefreshable
{
    private readonly IOperationsApi _operations;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    public ServerViewModel(IOperationsApi operations, IDialogService dialogs, INotificationService notifications)
    {
        _operations = operations;
        _dialogs = dialogs;
        _notifications = notifications;
        _selectedLevel = Levels[0];
    }

    public IReadOnlyList<LevelOption> Levels { get; } = new[]
    {
        new LevelOption(null, "All levels"),
        new LevelOption("WRN", "Warnings"),
        new LevelOption("ERR", "Errors"),
        new LevelOption("FTL", "Fatal"),
    };

    public IReadOnlyList<int> LineCounts { get; } = new[] { 100, 500, 2000 };

    public IReadOnlyList<NoticeLevel> NoticeLevels { get; } = Enum.GetValues<NoticeLevel>();

    public ObservableCollection<string> Entries { get; } = new();

    [ObservableProperty]
    private LevelOption _selectedLevel;

    [ObservableProperty]
    private int _lineCount = 500;

    [ObservableProperty]
    private string _logCaption = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendNoticeCommand))]
    private string _noticeMessage = string.Empty;

    [ObservableProperty]
    private NoticeLevel _noticeLevel = NoticeLevel.Info;

    public Task OnNavigatedToAsync(object? parameter) => LoadLogsAsync();

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    public Task RefreshAsync() => LoadLogsAsync();

    partial void OnSelectedLevelChanged(LevelOption value) => _ = LoadLogsAsync();

    partial void OnLineCountChanged(int value) => _ = LoadLogsAsync();

    private bool CanSendNotice() =>
        !string.IsNullOrWhiteSpace(NoticeMessage) && NoticeMessage.Trim().Length <= ServerNoticeRequest.MaxMessageLength;

    [RelayCommand(CanExecute = nameof(CanSendNotice))]
    private async Task SendNoticeAsync()
    {
        var message = NoticeMessage.Trim();
        if (!await _dialogs.ConfirmAsync("Send server notice", $"Show this {NoticeLevel.ToString().ToLowerInvariant()} on every connected terminal?\n\n{message}", "Send"))
        {
            return;
        }

        var result = await _operations.SendNoticeAsync(new ServerNoticeRequest { Level = NoticeLevel, Message = message });
        if (result.Success)
        {
            _notifications.Success("Notice sent to every connected terminal.");
            NoticeMessage = string.Empty;
        }
        else
        {
            _notifications.Error(result.Message, result.CorrelationId);
        }
    }

    private async Task LoadLogsAsync()
    {
        ErrorMessage = null;
        var result = await _operations.GetLogsAsync(LineCount, SelectedLevel.Code);
        if (!result.Success || result.Data is null)
        {
            ErrorMessage = result.Message;
            return;
        }

        // Newest first: the reason for a problem the technician was just called about is at the top.
        Entries.Clear();
        foreach (var entry in result.Data.Lines.Reverse())
        {
            Entries.Add(entry);
        }

        LogCaption = string.IsNullOrEmpty(result.Data.FileName)
            ? "No API log file yet."
            : string.Create(CultureInfo.CurrentCulture,
                $"{result.Data.FileName} · last written {result.Data.LastWriteUtc.ToLocalTime():dd MMM HH:mm:ss} · {Entries.Count} entries");
    }
}
