using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Identity;

namespace HotelPOS.Domain.Tests.Identity;

public class RefreshTokenTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsActive_BeforeExpiryAndNotRevoked_IsTrue()
    {
        var token = new RefreshToken(1, "hash", null, Now.AddHours(1), null);

        token.IsActive(Now).Should().BeTrue();
    }

    [Fact]
    public void IsActive_AfterExpiry_IsFalse()
    {
        var token = new RefreshToken(1, "hash", null, Now.AddMinutes(-1), null);

        token.IsActive(Now).Should().BeFalse();
    }

    [Fact]
    public void Revoke_MakesTokenInactive_AndKeepsFirstReason()
    {
        var token = new RefreshToken(1, "hash", null, Now.AddHours(1), null);

        token.Revoke(Now, "Rotated", "next-hash");
        token.Revoke(Now.AddMinutes(1), "Logout");

        token.IsActive(Now).Should().BeFalse();
        token.RevokedReason.Should().Be("Rotated");
        token.ReplacedByTokenHash.Should().Be("next-hash");
        token.RevokedAt.Should().Be(Now);
    }
}

public class DeviceTests
{
    [Fact]
    public void Constructor_NormalizesNameToUpperCase()
    {
        var device = new Device(Guid.NewGuid(), " waiter-01 ", DeviceType.Waiter, DateTime.UtcNow);

        device.Name.Should().Be("WAITER-01");
        device.IsActive.Should().BeTrue();
    }

    [Fact]
    public void UpdateRegistration_TruncatesLongMachineName()
    {
        var device = new Device(Guid.NewGuid(), "KITCHEN-01", DeviceType.Kitchen, DateTime.UtcNow);

        device.UpdateRegistration(DeviceType.Kitchen, new string('x', 150), "1.0.0", stationId: 2);

        device.MachineName.Should().HaveLength(100);
        device.AppVersion.Should().Be("1.0.0");
        device.StationId.Should().Be(2);
    }
}
