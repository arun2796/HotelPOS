using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Auth.Commands;
using HotelPOS.Application.Features.Licensing;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using HotelPOS.Domain.Licensing;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Licensing;

public class LicenseServiceTests : DatabaseTestBase
{
    private const string Machine = "HP-AAAABBBBCCCCDDDDEEEE";

    private static readonly (string PrivateKey, string PublicKey) Pair = LicenseKeyCodec.GenerateKeyPair();

    public LicenseServiceTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    private sealed class FixedMachine : IMachineIdProvider
    {
        public string GetMachineId() => Machine;
    }

    private sealed class FixedTrust : ILicenseTrust
    {
        public string PublicKey => Pair.PublicKey;
    }

    private static string Key(int terminals, DateOnly? expires, string machine = Machine) =>
        LicenseKeyCodec.Sign(new LicensePayload("L1", "Test Hotel", machine, "Standard", terminals, new DateOnly(2026, 1, 1), expires), Pair.PrivateKey);

    private async Task<T> WithServiceAsync<T>(Func<ILicenseService, AppDbContext, Task<T>> action)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new LicenseService(db, Clock, new FixedMachine(), new FixedTrust());
        return await action(service, db);
    }

    private async Task StartTrialAsync(DateTime startedAtUtc)
    {
        await QueryAsync(async db =>
        {
            db.ServerLicenses.Add(new ServerLicense(startedAtUtc));
            await db.SaveChangesAsync();
            return 0;
        });
    }

    private async Task RegisterDevicesAsync(params string[] names)
    {
        await QueryAsync(async db =>
        {
            foreach (var name in names)
            {
                db.Devices.Add(new Device(Guid.NewGuid(), name, DeviceType.Waiter, Clock.UtcNow));
                Clock.Advance(TimeSpan.FromMinutes(1));
            }

            await db.SaveChangesAsync();
            return 0;
        });
    }

    [Fact]
    public async Task FreshServer_IsInASevenDayTrial_ThatThenExpires()
    {
        await StartTrialAsync(Clock.UtcNow);

        var start = await WithServiceAsync((s, _) => s.GetStatusAsync(default));
        Clock.Advance(TimeSpan.FromDays(8));
        var later = await WithServiceAsync((s, _) => s.GetStatusAsync(default));

        start.State.Should().Be(LicenseStateNames.Trial);
        start.DaysRemaining.Should().Be(7);
        start.MaxTerminals.Should().Be(LicenseLimits.TrialTerminals);
        start.ShowWarning.Should().BeTrue();
        later.State.Should().Be(LicenseStateNames.Expired);
        later.IsExpired.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyKey_ForThisMachine_StartsALicence()
    {
        var status = await WithServiceAsync(async (s, _) => (await s.ApplyAsync(Key(3, new DateOnly(2027, 1, 1)), 1, default)).Value);

        status.State.Should().Be(LicenseStateNames.Licensed);
        status.MaxTerminals.Should().Be(3);
        status.Customer.Should().Be("Test Hotel");
        status.ExpiresOn.Should().Be(new DateOnly(2027, 1, 1));
        status.MachineId.Should().Be(Machine);
        status.Message.Should().BeNull();

        // Stored: a new scope reads it back.
        (await WithServiceAsync((s, _) => s.GetStatusAsync(default))).State.Should().Be(LicenseStateNames.Licensed);
    }

    [Fact]
    public async Task ApplyKey_ForAnotherMachine_ForgedOrExpired_IsRefused_AndChangesNothing()
    {
        await StartTrialAsync(Clock.UtcNow);
        var forged = Key(3, null)[..^4] + "AAAA";

        var wrongMachine = await WithServiceAsync((s, _) => s.ApplyAsync(Key(3, null, "HP-00000000000000000000"), 1, default));
        var tampered = await WithServiceAsync((s, _) => s.ApplyAsync(forged, 1, default));
        var expired = await WithServiceAsync((s, _) => s.ApplyAsync(Key(3, new DateOnly(2020, 1, 1)), 1, default));

        wrongMachine.Error!.Code.Should().Be(ErrorCodes.LicenseInvalid);
        wrongMachine.Error.Message.Should().Contain("another PC");
        tampered.Error!.Code.Should().Be(ErrorCodes.LicenseInvalid);
        expired.Error!.Code.Should().Be(ErrorCodes.LicenseInvalid);
        (await WithServiceAsync((s, _) => s.GetStatusAsync(default))).State.Should().Be(LicenseStateNames.Trial);
    }

    [Fact]
    public async Task StoredKey_ThatNoLongerFits_FallsBackToTheTrialClock()
    {
        // A database restored onto another PC: the key is for a different machine, and the trial ended long ago.
        await StartTrialAsync(Clock.UtcNow.AddDays(-30));
        await QueryAsync(async db =>
        {
            db.ServerLicenses.Single().Apply(Key(9, null, "HP-00000000000000000000"), Clock.UtcNow, null);
            await db.SaveChangesAsync();
            return 0;
        });

        var status = await WithServiceAsync((s, _) => s.GetStatusAsync(default));

        status.IsExpired.Should().BeTrue();
        status.Message.Should().Contain("another PC");
    }

    [Fact]
    public async Task Terminals_AreLimitedToTheLicensedCount_InRegistrationOrder()
    {
        await StartTrialAsync(Clock.UtcNow);
        await WithServiceAsync((s, _) => s.ApplyAsync(Key(2, null), 1, default));
        await RegisterDevicesAsync("T-ONE", "T-TWO");

        var existing = await WithServiceAsync((s, _) => s.CheckTerminalAsync("t-one", default));
        var third = await WithServiceAsync((s, _) => s.CheckTerminalAsync("T-THREE", default));

        existing.Should().BeNull();
        third!.Code.Should().Be(ErrorCodes.LicenseTerminalLimit);
        third.Message.Should().Contain("2 terminal");

        // Lowering the licence to one terminal keeps the oldest and refuses the other.
        await WithServiceAsync((s, _) => s.ApplyAsync(Key(1, null), 1, default));
        (await WithServiceAsync((s, _) => s.CheckTerminalAsync("T-ONE", default))).Should().BeNull();
        (await WithServiceAsync((s, _) => s.CheckTerminalAsync("T-TWO", default)))!.Code.Should().Be(ErrorCodes.LicenseTerminalLimit);
    }

    [Fact]
    public async Task Login_OverTheTerminalLimit_IsRefused_WithoutRegisteringTheTerminal()
    {
        await StartTrialAsync(Clock.UtcNow);
        await RegisterDevicesAsync(Enumerable.Range(1, LicenseLimits.TrialTerminals).Select(i => $"PC-{i:00}").ToArray());

        var refused = await SendAsync(new LoginCommand(Login("admin", DatabaseFixture.AdminPassword, "PC-99")));
        var known = await SendAsync(new LoginCommand(Login("admin", DatabaseFixture.AdminPassword, "PC-01")));

        refused.Error!.Code.Should().Be(ErrorCodes.LicenseTerminalLimit);
        known.IsSuccess.Should().BeTrue(known.Error?.Message);
        (await QueryAsync(db => db.Devices.AnyAsync(d => d.Name == "PC-99"))).Should().BeFalse();
    }

    [Fact]
    public async Task Login_AfterExpiry_IsOpenToAdministratorsOnly()
    {
        await StartTrialAsync(Clock.UtcNow.AddDays(-30));
        await CreateUserAsync("cashier1", "secret1", Roles.Cashier);

        var cashier = await SendAsync(new LoginCommand(Login("cashier1", "secret1")));
        var admin = await SendAsync(new LoginCommand(Login("admin", DatabaseFixture.AdminPassword)));

        cashier.Error!.Code.Should().Be(ErrorCodes.LicenseExpired);
        admin.IsSuccess.Should().BeTrue(admin.Error?.Message);
    }

    [Fact]
    public async Task ApplyLicenseCommand_NeedsThePermission_AndRejectsAKeyOfAnotherVendor()
    {
        await StartTrialAsync(Clock.UtcNow);
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);

        // The embedded vendor key does not match a test key: the command refuses it with the licence error.
        var refused = await SendAsync(new ApplyLicenseCommand(new ApplyLicenseRequest { Key = Key(3, null) }));
        CurrentUser.SignIn(await AdminIdAsync(), "cashier", Roles.Cashier);
        var forbidden = await SendAsync(new ApplyLicenseCommand(new ApplyLicenseRequest { Key = Key(3, null) }));

        refused.Error!.Code.Should().Be(ErrorCodes.LicenseInvalid);
        forbidden.Error!.Code.Should().Be(ErrorCodes.Forbidden);
    }

    private static LoginRequest Login(string username, string password, string? device = null) => new()
    {
        Username = username,
        Password = password,
        DeviceName = device,
        DeviceType = device is null ? null : DeviceType.Waiter,
        MachineName = "PC-1",
        AppVersion = "0.1.0",
    };
}
