using HotelPOS.Domain.Common;
using HotelPOS.Domain.Identity;

namespace HotelPOS.Domain.Tests.Identity;

public class UserTests
{
    private static Role NewRole(int id, string name)
    {
        var role = new Role(name, null, isSystem: true);
        role.Id = id;
        return role;
    }

    [Fact]
    public void Constructor_TrimsUsernameAndStoresNormalizedForm()
    {
        var user = new User("  Waiter1 ", "Arun", "hash", mustChangePassword: true);

        user.Username.Should().Be("Waiter1");
        user.NormalizedUsername.Should().Be("WAITER1");
        user.IsActive.Should().BeTrue();
        user.MustChangePassword.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankUsername_Throws(string username)
    {
        var act = () => new User(username, "Name", "hash", false);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SetRoles_AddsMissingAndRemovesExtraRoles()
    {
        var waiter = NewRole(3, "Waiter");
        var cashier = NewRole(5, "Cashier");
        var manager = NewRole(2, "Manager");
        var user = new User("u1", "User", "hash", false);
        user.SetRoles(new[] { waiter, cashier });

        user.SetRoles(new[] { cashier, manager });

        user.RoleNames.Should().BeEquivalentTo("Cashier", "Manager");
        user.HasRole("Waiter").Should().BeFalse();
    }

    [Fact]
    public void SetRoles_WithNoRoles_Throws()
    {
        var user = new User("u1", "User", "hash", false);

        var act = () => user.SetRoles(Array.Empty<Role>());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void RecordSuccessfulLogin_ResetsFailedLoginCount()
    {
        var user = new User("u1", "User", "hash", false);
        user.RecordFailedLogin();
        user.RecordFailedLogin();
        var now = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

        user.RecordSuccessfulLogin(now);

        user.FailedLoginCount.Should().Be(0);
        user.LastLoginAt.Should().Be(now);
    }

    [Fact]
    public void SetPasswordHash_UpdatesHashAndMustChangeFlag()
    {
        var user = new User("u1", "User", "old", mustChangePassword: true);
        var changedAt = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

        user.SetPasswordHash("new", mustChangePassword: false, changedAt);

        user.PasswordHash.Should().Be("new");
        user.MustChangePassword.Should().BeFalse();
        user.PasswordChangedAt.Should().Be(changedAt);
    }

    [Fact]
    public void IsLockedOut_WhenNoLockIsSet_IsFalse()
    {
        var user = new User("u1", "User", "hash", false);

        user.IsLockedOut(DateTime.UtcNow).Should().BeFalse();
    }
}
