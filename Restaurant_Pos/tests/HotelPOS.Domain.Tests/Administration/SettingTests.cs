using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Administration;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Tests.Administration;

public class SettingTests
{
    [Theory]
    [InlineData(SettingDataType.Int, "10", true)]
    [InlineData(SettingDataType.Int, "ten", false)]
    [InlineData(SettingDataType.Decimal, "12.50", true)]
    [InlineData(SettingDataType.Decimal, "twelve", false)]
    [InlineData(SettingDataType.Bool, "true", true)]
    [InlineData(SettingDataType.Bool, "yes", false)]
    [InlineData(SettingDataType.Time, "04:00", true)]
    [InlineData(SettingDataType.Time, "4 am", false)]
    [InlineData(SettingDataType.Json, "{\"a\":1}", true)]
    [InlineData(SettingDataType.Json, "{a:", false)]
    [InlineData(SettingDataType.String, "anything", true)]
    public void IsValidValue_ChecksTheDataType(SettingDataType type, string value, bool expected)
    {
        Setting.IsValidValue(type, value).Should().Be(expected);
    }

    [Fact]
    public void SetValue_WithInvalidValue_Throws()
    {
        var setting = new Setting("KitchenWarnMinutes", "10", SettingDataType.Int, null, true);

        var act = () => setting.SetValue("soon");

        act.Should().Throw<DomainException>();
        setting.Value.Should().Be("10");
    }

    [Fact]
    public void SetValue_TrimsWhitespace()
    {
        var setting = new Setting("RestaurantName", "x", SettingDataType.String, null, true);

        setting.SetValue("  Hotel Saravana  ");

        setting.Value.Should().Be("Hotel Saravana");
    }
}
