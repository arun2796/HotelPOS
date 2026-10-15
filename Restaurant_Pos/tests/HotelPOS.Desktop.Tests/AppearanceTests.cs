using System.Globalization;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Config;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Startup;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests;

public class IndianCultureTests
{
    [Theory]
    [InlineData("0", "0.00")]
    [InlineData("999.5", "999.50")]
    [InlineData("1234.5", "1,234.50")]
    [InlineData("123456", "1,23,456.00")]
    [InlineData("12345678.9", "1,23,45,678.90")]
    [InlineData("-250000", "-2,50,000.00")]
    public void Money_UsesIndianDigitGrouping(string amount, string expected)
    {
        InCulture(() => Money.Format(decimal.Parse(amount, CultureInfo.InvariantCulture)).Should().Be(expected));
    }

    [Fact]
    public void Money_ParsesWhatItFormats()
    {
        InCulture(() =>
        {
            Money.TryParse("1,23,456.50", out var amount).Should().BeTrue();
            amount.Should().Be(123456.50m);
        });
    }

    [Fact]
    public void Dates_AreDayMonthYear_WithTwentyFourHourTimes()
    {
        var culture = IndianCulture.Create();
        var moment = new DateTime(2026, 10, 4, 21, 5, 0);

        moment.ToString("d", culture).Should().Be("04-10-2026");
        moment.ToString("t", culture).Should().Be("21:05");
        culture.NumberFormat.CurrencySymbol.Should().Be("₹");
    }

    private static void InCulture(Action assertion)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = IndianCulture.Create();
        try
        {
            assertion();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}

public class DensityTests
{
    [Theory]
    [InlineData(DeviceType.Waiter, null, ThemeService.Comfort)]
    [InlineData(DeviceType.Kitchen, null, ThemeService.Comfort)]
    [InlineData(DeviceType.Billing, null, ThemeService.Compact)]
    [InlineData(DeviceType.Admin, null, ThemeService.Compact)]
    [InlineData(DeviceType.Waiter, "Compact", ThemeService.Compact)]
    [InlineData(DeviceType.Admin, "comfort", ThemeService.Comfort)]
    [InlineData(DeviceType.Billing, "unknown", ThemeService.Compact)]
    public void Density_FollowsTerminalType_UnlessChosen(DeviceType type, string? chosen, string expected)
    {
        ThemeService.ResolveDensity(new ClientSettings { DeviceType = type, Density = chosen }).Should().Be(expected);
    }

    [Fact]
    public async Task ChangingOnlyTheDensity_SavesIt_WithoutSigningOut()
    {
        var settings = InMemorySettings.Configured();
        var navigator = Substitute.For<IAppNavigator>();
        var theme = new ThemeService(settings);
        var vm = new ConfigurationViewModel(settings, Substitute.For<ISystemApi>(), navigator, Substitute.For<IDialogService>(), theme);
        await vm.OnNavigatedToAsync(new ConfigurationContext(IsFirstRun: false, InShell: true));
        vm.Density.Should().Be(ConfigurationViewModel.AutomaticDensity);

        vm.Density = ThemeService.Comfort;
        await vm.SaveCommand.ExecuteAsync(null);

        settings.Current.Density.Should().Be(ThemeService.Comfort);
        theme.CurrentDensity.Should().Be(ThemeService.Comfort);
        await navigator.DidNotReceiveWithAnyArgs().LogoutAsync(default!);

        vm.Density = ConfigurationViewModel.AutomaticDensity;
        await vm.SaveCommand.ExecuteAsync(null);
        settings.Current.Density.Should().BeNull();
    }
}

public class CompactMoneyTests
{
    [Theory]
    [InlineData("950", "950")]
    [InlineData("48230", "48.2K")]
    [InlineData("140400", "1.4L")]
    [InlineData("25000000", "2.5Cr")]
    public void Compact_UsesIndianUnits(string amount, string expected)
    {
        Money.Compact(decimal.Parse(amount, CultureInfo.InvariantCulture)).Should().Be(expected);
    }

    [Fact]
    public void RingSegments_CoverTheCircle()
    {
        var half = HotelPOS.Desktop.Controls.ChartGeometry.RingSegment(0, 180, 72, 20);
        var full = HotelPOS.Desktop.Controls.ChartGeometry.RingSegment(0, 360, 72, 20);

        half.Bounds.Width.Should().BeApproximately(72, 0.5, "the right half of the ring spans one radius");
        full.Bounds.Width.Should().BeApproximately(144, 0.5);
    }
}
