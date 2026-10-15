using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Shifts.Commands;
using HotelPOS.Application.Features.Users;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Users;
using HotelPOS.Domain.Identity;
using HotelPOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Support;

[Collection(DatabaseCollection.Name)]
public abstract class DatabaseTestBase : IAsyncLifetime
{
    private AsyncServiceScope _scope;

    protected DatabaseTestBase(DatabaseFixture fixture)
    {
        Fixture = fixture;
    }

    protected DatabaseFixture Fixture { get; }

    protected TestClock Clock => Fixture.Clock;

    protected TestCurrentUser CurrentUser => Fixture.CurrentUser;

    protected RecordingRealtimeNotifier Realtime => Fixture.Realtime;

    public virtual async Task InitializeAsync()
    {
        await Fixture.ResetAsync();
        _scope = Fixture.Services.CreateAsyncScope();
    }

    public async Task DisposeAsync() => await _scope.DisposeAsync();

    protected T Get<T>()
        where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    // A fresh scope per request, like one HTTP request: its own DbContext, transaction and after-commit queue.
    protected async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    // For a step another role takes in the middle of a scenario (a manager changing the menu while a waiter
    // orders): requests check permissions, so it runs as the admin and the signed-in user is restored afterwards.
    protected async Task<TResponse> SendAsAdminAsync<TResponse>(IRequest<TResponse> request)
    {
        var (userId, userName, roles, permissions) = (CurrentUser.UserId, CurrentUser.UserName, CurrentUser.Roles, CurrentUser.Permissions);
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Contracts.Security.Roles.Admin);
        try
        {
            return await SendAsync(request);
        }
        finally
        {
            (CurrentUser.UserId, CurrentUser.UserName, CurrentUser.Roles, CurrentUser.Permissions) = (userId, userName, roles, permissions);
        }
    }

    protected async Task<TResult> QueryAsync<TResult>(Func<AppDbContext, Task<TResult>> query)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await query(db);
    }

    protected Task<int> AdminIdAsync() =>
        QueryAsync(db => db.Users.Where(u => u.NormalizedUsername == "ADMIN").Select(u => u.Id).SingleAsync());

    // Cash needs an open shift on the terminal (phase 13). Opens one on the terminal for the given user; the signed-in
    // user is restored afterwards.
    protected async Task<CashShiftDto> OpenShiftAsync(Guid deviceId, int userId, string userName, string role, params DenominationCountDto[] counts)
    {
        var (signedIn, name, roles, permissions, device) = (CurrentUser.UserId, CurrentUser.UserName, CurrentUser.Roles, CurrentUser.Permissions, CurrentUser.DeviceId);
        CurrentUser.SignIn(userId, userName, role);
        CurrentUser.DeviceId = deviceId;
        try
        {
            var opened = await SendAsync(new OpenShiftCommand(new OpenShiftRequest { Counts = counts }));
            opened.IsSuccess.Should().BeTrue(opened.Error?.Message);
            return opened.Value;
        }
        finally
        {
            (CurrentUser.UserId, CurrentUser.UserName, CurrentUser.Roles, CurrentUser.Permissions, CurrentUser.DeviceId) = (signedIn, name, roles, permissions, device);
        }
    }

    protected async Task EnsureDeviceAsync(Guid deviceId, string? name = null)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.Devices.AnyAsync(d => d.Id == deviceId))
        {
            db.Devices.Add(new Device(deviceId, name ?? "COUNTER-" + deviceId.ToString("N")[..6], DeviceType.Billing, Clock.UtcNow));
            await db.SaveChangesAsync();
        }
    }

    // Seeds a user straight into the database, as DbSeeder does. Scenarios use short demo passwords ("secret1") that
    // the create-user request has always rejected, so they cannot be created through it; Users/UserTests covers it.
    protected async Task<UserDto> CreateUserAsync(string username, string password, params string[] roles)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = new User(username, username, hasher.Hash(password), mustChangePassword: false);
        user.SetRoles(await db.Roles.Where(r => roles.Contains(r.Name)).ToListAsync());
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return UserMapping.ToDto(user);
    }
}
