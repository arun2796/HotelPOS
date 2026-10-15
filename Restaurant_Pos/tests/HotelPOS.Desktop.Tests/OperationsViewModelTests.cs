using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Admin;
using HotelPOS.Desktop.Modules.Auth;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Shell;
using HotelPOS.Desktop.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using HotelPOS.Desktop.Services.Ui;
using NSubstitute;

namespace HotelPOS.Desktop.Tests;

public class OperationsViewModelTests
{
    private readonly IOperationsApi _operations = Substitute.For<IOperationsApi>();
    private readonly IAdminSettingsApi _settings = Substitute.For<IAdminSettingsApi>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    [Theory]
    [InlineData("1.2.0", "1.2.0", true)]
    [InlineData("1.3.0", "1.2.9", true)]
    [InlineData("1.1.9", "1.2.0", false)]
    [InlineData("1.2.0-beta+abc", "1.2.0", true)]
    [InlineData("0.1.0", "", true)]
    [InlineData("0.1.0", "not-a-version", true)]
    public void VersionGate_BlocksOnlyClientsBelowTheMinimum(string client, string minimum, bool supported)
    {
        VersionGate.IsSupported(client, minimum).Should().Be(supported);
    }

    [Fact]
    public async Task Startup_WithAServerNeedingANewerClient_ShowsTheUpdateScreen_InsteadOfTheLogin()
    {
        var systemApi = Substitute.For<ISystemApi>();
        var navigator = Substitute.For<IAppNavigator>();
        var info = new SystemInfoDto { RestaurantName = "Test", MinClientVersion = "99.0.0", ApiVersion = "99.0.1" };
        systemApi.GetInfoAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(ApiResult<SystemInfoDto>.Ok(info));
        var flow = new StartupFlow(InMemorySettings.Configured(), new AuthSession(new InMemorySecureStore()), Substitute.For<IAuthApi>(),
            systemApi, navigator, NullLogger<StartupFlow>.Instance);

        await flow.RunAsync();

        await navigator.Received(1).ShowUpdateRequiredAsync(info);
        await navigator.DidNotReceiveWithAnyArgs().ShowLoginAsync();
    }

    [Fact]
    public async Task Backup_ShowsTheLastFailure_AndBackupNowReloads()
    {
        var failed = new BackupDto { Id = 2, FileName = "hotelpos-2.dump", Status = BackupStatus.Failed, Error = "disk full", StartedAtUtc = DateTime.UtcNow };
        var ok = new BackupDto { Id = 3, FileName = "hotelpos-3.dump", Status = BackupStatus.Succeeded, SizeBytes = 3 * 1024 * 1024, StartedAtUtc = DateTime.UtcNow };
        _operations.GetBackupsAsync(Arg.Any<CancellationToken>()).Returns(
            ApiResult<BackupOverviewDto>.Ok(new BackupOverviewDto { Folder = @"D:\Backups", Time = "02:00", RetentionDays = 7, LastFailure = failed, Backups = new[] { failed } }),
            ApiResult<BackupOverviewDto>.Ok(new BackupOverviewDto { Folder = @"D:\Backups", Time = "02:00", RetentionDays = 7, Backups = new[] { ok, failed } }));
        _operations.BackupNowAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<BackupDto>.Ok(ok));
        var vm = new BackupViewModel(_operations, _settings, _notifications);

        await vm.OnNavigatedToAsync(null);
        vm.FailureText.Should().Contain("disk full");
        vm.Time.Should().Be("02:00");

        await vm.BackupNowCommand.ExecuteAsync(null);

        _notifications.Received(1).Success(Arg.Is<string>(m => m.Contains("hotelpos-3.dump")));
        vm.HasFailure.Should().BeFalse("the newer success resolved it");
        vm.Backups.Select(b => b.Size).Should().Equal("3.0 MB", "—");
    }

    [Theory]
    [InlineData("3:30", "14")]
    [InlineData("25:00", "14")]
    [InlineData("03:30", "0")]
    public async Task Backup_RejectsInvalidSettings_BeforeCallingTheServer(string time, string days)
    {
        _operations.GetBackupsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<BackupOverviewDto>.Ok(new BackupOverviewDto { Time = "03:30", RetentionDays = 14 }));
        var vm = new BackupViewModel(_operations, _settings, _notifications);
        await vm.OnNavigatedToAsync(null);
        vm.Time = time;
        vm.RetentionDays = days;

        await vm.SaveSettingsCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        await _settings.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Backup_SavesItsFourSettings()
    {
        _operations.GetBackupsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<BackupOverviewDto>.Ok(new BackupOverviewDto { Time = "03:30", RetentionDays = 14, Enabled = true }));
        _settings.UpdateAsync(Arg.Any<UpdateSettingsRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<SettingDto>>.Ok(new List<SettingDto>()));
        var vm = new BackupViewModel(_operations, _settings, _notifications);
        await vm.OnNavigatedToAsync(null);
        vm.Time = "02:15";
        vm.RetentionDays = "30";
        vm.Enabled = false;
        vm.Folder = @" E:\HotelPOS ";

        await vm.SaveSettingsCommand.ExecuteAsync(null);

        await _settings.Received(1).UpdateAsync(Arg.Is<UpdateSettingsRequest>(r =>
            r.Items.Single(i => i.Key == SettingKeys.BackupTime).Value == "02:15"
            && r.Items.Single(i => i.Key == SettingKeys.BackupRetentionDays).Value == "30"
            && r.Items.Single(i => i.Key == SettingKeys.BackupEnabled).Value == "false"
            && r.Items.Single(i => i.Key == SettingKeys.BackupPath).Value == @"E:\HotelPOS"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Server_ShowsNewestLogEntriesFirst_AndSendsANoticeOnlyAfterConfirmation()
    {
        _operations.GetLogsAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(
            ApiResult<LogTailDto>.Ok(new LogTailDto { FileName = "api-1.log", Lines = new[] { "first", "second", "third" } }));
        _operations.SendNoticeAsync(Arg.Any<ServerNoticeRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<object>.Ok(null));
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(false, true);
        var vm = new ServerViewModel(_operations, _dialogs, _notifications);

        await vm.OnNavigatedToAsync(null);
        vm.Entries.Should().Equal("third", "second", "first");

        vm.SendNoticeCommand.CanExecute(null).Should().BeFalse();
        vm.NoticeMessage = "Server restarting in 2 minutes";
        vm.NoticeLevel = NoticeLevel.Warning;
        await vm.SendNoticeCommand.ExecuteAsync(null);
        await _operations.DidNotReceiveWithAnyArgs().SendNoticeAsync(default!, default);

        await vm.SendNoticeCommand.ExecuteAsync(null);
        await _operations.Received(1).SendNoticeAsync(
            Arg.Is<ServerNoticeRequest>(r => r.Level == NoticeLevel.Warning && r.Message == "Server restarting in 2 minutes"), Arg.Any<CancellationToken>());
        vm.NoticeMessage.Should().BeEmpty();
    }
}
