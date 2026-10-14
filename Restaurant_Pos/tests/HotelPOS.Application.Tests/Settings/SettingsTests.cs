using HotelPOS.Application.Common;
using HotelPOS.Application.Features.Operations.Commands;
using HotelPOS.Application.Features.Operations.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Settings;

public class SettingsTests : DatabaseTestBase
{
    public SettingsTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    // Settings are maintained from the admin PC.
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
    }

    [Fact]
    public async Task Seed_CreatesDefaultSettings()
    {
        var all = (await SendAsync(new GetAllSettingsQuery())).Value;

        all.Select(s => s.Key).Should().Contain(new[] { SettingKeys.RestaurantName, SettingKeys.CurrencySymbol, SettingKeys.BusinessDayStartTime });
    }

    [Fact]
    public async Task GetPublic_ExcludesPrivateSettings()
    {
        var all = (await SendAsync(new GetPublicSettingsQuery())).Value;

        all.Should().OnlyContain(s => s.IsPublic);
        all.Select(s => s.Key).Should().NotContain(SettingKeys.InvoicePrefix);
    }

    [Fact]
    public async Task Update_WithValidValues_SavesAndAuditsOldAndNew()
    {
        var result = await SendAsync(new UpdateSettingsCommand(new UpdateSettingsRequest
        {
            Items = new[]
            {
                new SettingValue { Key = SettingKeys.RestaurantName, Value = "Hotel Saravana" },
                new SettingValue { Key = SettingKeys.KitchenWarnMinutes, Value = "12" },
            },
        }));

        result.IsSuccess.Should().BeTrue();
        result.Value.Single(s => s.Key == SettingKeys.RestaurantName).Value.Should().Be("Hotel Saravana");
        var audit = await QueryAsync(db => db.AuditLogs.SingleAsync(a => a.Action == AuditActions.SettingsUpdated));
        audit.OldValues.Should().Contain("HotelPOS Restaurant");
        audit.NewValues.Should().Contain("Hotel Saravana");
        (await SendAsync(new GetSystemInfoQuery())).Value.RestaurantName.Should().Be("Hotel Saravana");
    }

    [Fact]
    public async Task Update_WithWrongTypeOrUnknownKey_RejectsTheWholeRequest()
    {
        var result = await SendAsync(new UpdateSettingsCommand(new UpdateSettingsRequest
        {
            Items = new[]
            {
                new SettingValue { Key = SettingKeys.RestaurantName, Value = "Should not be saved" },
                new SettingValue { Key = SettingKeys.KitchenWarnMinutes, Value = "ten" },
                new SettingValue { Key = "NoSuchKey", Value = "x" },
            },
        }));

        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        result.Error.Details.Select(d => d.Field).Should().BeEquivalentTo(SettingKeys.KitchenWarnMinutes, "NoSuchKey");
        (await SendAsync(new GetSystemInfoQuery())).Value.RestaurantName.Should().Be("HotelPOS Restaurant");
    }
}
