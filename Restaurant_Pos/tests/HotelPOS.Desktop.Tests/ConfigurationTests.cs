using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Config;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests;

public class ApiUrlTests
{
    [Theory]
    [InlineData("192.168.1.100:5000", "http://192.168.1.100:5000")]
    [InlineData("http://192.168.1.100:5000/", "http://192.168.1.100:5000")]
    [InlineData("  https://hotelpos-server:5001  ", "https://hotelpos-server:5001")]
    [InlineData("http://server/pos/", "http://server/pos")]
    public void TryNormalize_AcceptsCommonForms(string input, string expected)
    {
        ApiUrl.TryNormalize(input, out var normalized, out var error).Should().BeTrue(error);
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://192.168.1.100")]
    [InlineData("http://")]
    [InlineData("http://server:5000/?x=1")]
    public void TryNormalize_RejectsInvalidAddresses(string input)
    {
        ApiUrl.TryNormalize(input, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }
}

public class ConfigurationViewModelTests
{
    private readonly InMemorySettings _settings = new();
    private readonly ISystemApi _systemApi = Substitute.For<ISystemApi>();
    private readonly IAppNavigator _navigator = Substitute.For<IAppNavigator>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private ConfigurationViewModel Create() => new(_settings, _systemApi, _navigator, _dialogs);

    [Theory]
    [InlineData("192.168.1.100:5000", "WAITER-01", true)]
    [InlineData("not a url at all", "WAITER-01", false)]
    [InlineData("192.168.1.100:5000", "W", false)]
    [InlineData("192.168.1.100:5000", "WAITER 01", false)]
    public void Validate_ChecksAddressAndTerminalName(string url, string name, bool valid)
    {
        (ConfigurationViewModel.Validate(url, name, out _) is null).Should().Be(valid);
    }

    [Fact]
    public async Task FirstRun_SuggestsATerminalNameForTheType()
    {
        var vm = Create();
        await vm.OnNavigatedToAsync(new ConfigurationContext(IsFirstRun: true, InShell: false));

        vm.DeviceName.Should().Be("WAITER-01");
        vm.DeviceType = DeviceType.Kitchen;
        vm.DeviceName.Should().Be("KITCHEN-01");
        vm.CanCancel.Should().BeFalse();
    }

    [Fact]
    public async Task Save_WithInvalidAddress_ShowsMessage_AndSavesNothing()
    {
        var vm = Create();
        await vm.OnNavigatedToAsync(new ConfigurationContext(true, false));
        vm.ApiBaseUrl = "ftp://wrong";

        await vm.SaveCommand.ExecuteAsync(null);

        vm.ValidationMessage.Should().NotBeNullOrEmpty();
        _settings.SaveCount.Should().Be(0);
        await _navigator.DidNotReceiveWithAnyArgs().ShowLoginAsync();
    }

    [Fact]
    public async Task Save_OnFirstRun_StoresNormalizedValues_AndGoesToLogin()
    {
        var vm = Create();
        await vm.OnNavigatedToAsync(new ConfigurationContext(true, false));
        vm.ApiBaseUrl = "192.168.1.100:5000";
        vm.DeviceName = "billing-02";
        vm.DeviceType = DeviceType.Billing;

        await vm.SaveCommand.ExecuteAsync(null);

        _settings.Current.ApiBaseUrl.Should().Be("http://192.168.1.100:5000");
        _settings.Current.DeviceName.Should().Be("BILLING-02");
        _settings.Current.DeviceType.Should().Be(DeviceType.Billing);
        await _navigator.Received(1).ShowLoginAsync(Arg.Any<string?>());
    }

    [Fact]
    public async Task Save_WithANewTerminalName_ForgetsTheOldDeviceId()
    {
        _settings.Save(new ClientSettings { ApiBaseUrl = "http://server:5000", DeviceName = "WAITER-01", DeviceId = Guid.NewGuid() });
        var vm = Create();
        await vm.OnNavigatedToAsync(new ConfigurationContext(false, false));
        vm.DeviceName = "WAITER-02";

        await vm.SaveCommand.ExecuteAsync(null);

        _settings.Current.DeviceId.Should().BeNull();
    }

    [Fact]
    public async Task Save_InShell_WhenUserDeclines_DoesNotSaveOrSignOut()
    {
        _settings.Save(new ClientSettings { ApiBaseUrl = "http://server:5000", DeviceName = "ADMIN-01" });
        var saves = _settings.SaveCount;
        _dialogs.ConfirmAsync(default!, default!).ReturnsForAnyArgs(false);
        var vm = Create();
        await vm.OnNavigatedToAsync(null);
        vm.ApiBaseUrl = "http://other-server:5000";

        await vm.SaveCommand.ExecuteAsync(null);

        _settings.SaveCount.Should().Be(saves);
        await _navigator.DidNotReceiveWithAnyArgs().LogoutAsync();
    }

    [Fact]
    public async Task Save_InShell_WhenConfirmed_SavesAndSignsOut()
    {
        _settings.Save(new ClientSettings { ApiBaseUrl = "http://server:5000", DeviceName = "ADMIN-01" });
        _dialogs.ConfirmAsync(default!, default!).ReturnsForAnyArgs(true);
        var vm = Create();
        await vm.OnNavigatedToAsync(null);
        vm.ApiBaseUrl = "http://other-server:5000";

        await vm.SaveCommand.ExecuteAsync(null);

        _settings.Current.ApiBaseUrl.Should().Be("http://other-server:5000");
        await _navigator.Received(1).LogoutAsync(Arg.Any<string?>());
    }

    [Fact]
    public async Task TestConnection_ReportsTheRestaurantName()
    {
        _systemApi.GetInfoAsync("http://192.168.1.100:5000", Arg.Any<CancellationToken>())
            .Returns(ApiResult<SystemInfoDto>.Ok(new SystemInfoDto { RestaurantName = "Hotel Saravana", ApiVersion = "0.1.0" }));
        var vm = Create();
        await vm.OnNavigatedToAsync(new ConfigurationContext(true, false));
        vm.ApiBaseUrl = "192.168.1.100:5000";

        await vm.TestConnectionCommand.ExecuteAsync(null);

        vm.TestSucceeded.Should().BeTrue();
        vm.TestResult.Should().Contain("Hotel Saravana");
    }

    [Fact]
    public async Task TestConnection_WhenNothingAnswers_ExplainsWhatToCheck()
    {
        _systemApi.GetInfoAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<SystemInfoDto>.ConnectionFailure("down"));
        var vm = Create();
        await vm.OnNavigatedToAsync(new ConfigurationContext(true, false));
        vm.ApiBaseUrl = "192.168.1.100:5000";

        await vm.TestConnectionCommand.ExecuteAsync(null);

        vm.TestSucceeded.Should().BeFalse();
        vm.TestResult.Should().Contain("firewall");
    }
}

public class ClientSettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hotelpos-desktop-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTheSettings()
    {
        var paths = new AppPaths("default", Path.Combine(_root, "machine"), Path.Combine(_root, "user"));
        var service = new ClientSettingsService(paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClientSettingsService>.Instance);
        var deviceId = Guid.NewGuid();

        service.Save(new ClientSettings { ApiBaseUrl = "http://10.0.0.5:5000", DeviceName = "KITCHEN-01", DeviceType = DeviceType.Kitchen, DeviceId = deviceId });
        var reloaded = new ClientSettingsService(paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClientSettingsService>.Instance).Current;

        reloaded.ApiBaseUrl.Should().Be("http://10.0.0.5:5000");
        reloaded.DeviceType.Should().Be(DeviceType.Kitchen);
        reloaded.DeviceId.Should().Be(deviceId);
        File.ReadAllText(paths.MachineSettingsFile).Should().Contain("\"deviceType\": \"Kitchen\"").And.NotContain("password");
    }

    [Fact]
    public void Load_WithACorruptFile_StartsUnconfiguredInsteadOfCrashing()
    {
        var paths = new AppPaths("default", Path.Combine(_root, "machine"), Path.Combine(_root, "user"));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.MachineSettingsFile)!);
        File.WriteAllText(paths.MachineSettingsFile, "{ this is not json");

        var service = new ClientSettingsService(paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClientSettingsService>.Instance);

        service.Current.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void Profiles_UseSeparateFiles()
    {
        var a = new AppPaths("default", "m", "u");
        var b = AppPaths.FromArgs(new[] { "--profile", "Waiter2" });

        b.Profile.Should().Be("waiter2");
        b.MachineSettingsFile.Should().EndWith("settings.waiter2.json");
        a.MachineSettingsFile.Should().EndWith("settings.json");
        AppPaths.FromArgs(new[] { "--profile", "../evil" }).Profile.Should().Be("default");
    }
}
