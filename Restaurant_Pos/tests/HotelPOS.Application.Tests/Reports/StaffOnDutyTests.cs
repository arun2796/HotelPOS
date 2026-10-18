using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Application.Features.Reports.Queries;

namespace HotelPOS.Application.Tests.Reports;

public class StaffOnDutyMergeTests
{
    private static readonly DateTime Since = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private static PresenceEntry Conn(int id, string name, string role, string? device, int minutes = 0) =>
        new(id, name, new[] { role }, Guid.NewGuid(), device, Since.AddMinutes(minutes));

    [Fact]
    public void Compose_MergesThreeSourcesIntoOneRowPerUser_OnlineFirstThenByName()
    {
        var presence = new[]
        {
            Conn(1, "Zed", "Manager", "OFFICE", 5),
            Conn(2, "Asha", "Waiter", "PHONE-1", 10),
            Conn(2, "Asha", "Waiter", "TABLET-2", 2),
        };
        var duty = new[]
        {
            new StaffDutyRow { UserId = 3, UserName = "Meena", Roles = "Cashier", HasOpenShift = true, ShiftDeviceName = "BILLING-01" },
            new StaffDutyRow { UserId = 4, UserName = "Bala", Roles = "Waiter", ActiveOrders = 2 },
            new StaffDutyRow { UserId = 2, UserName = "Asha", Roles = "Waiter", ActiveOrders = 1 },
        };

        var rows = StaffOnDutyMerge.Compose(presence, duty);

        rows.Select(r => r.UserName).Should().Equal("Asha", "Zed", "Bala", "Meena");
        var asha = rows[0];
        asha.IsOnline.Should().BeTrue();
        asha.Devices.Should().Equal("PHONE-1", "TABLET-2");
        asha.ConnectedSinceUtc.Should().Be(Since.AddMinutes(2), "the earliest connection");
        asha.ActiveOrders.Should().Be(1, "presence and active orders merge into the same row");
        asha.HasOpenShift.Should().BeFalse();
        rows[2].IsOnline.Should().BeFalse();
        rows[2].ActiveOrders.Should().Be(2);
        rows[3].HasOpenShift.Should().BeTrue();
        rows[3].ShiftDeviceName.Should().Be("BILLING-01");
        rows[3].Roles.Should().Equal("Cashier");
    }

    [Fact]
    public void Compose_ReturnsEmpty_WhenNobodyIsOnDuty() =>
        StaffOnDutyMerge.Compose(Array.Empty<PresenceEntry>(), Array.Empty<StaffDutyRow>()).Should().BeEmpty();
}
