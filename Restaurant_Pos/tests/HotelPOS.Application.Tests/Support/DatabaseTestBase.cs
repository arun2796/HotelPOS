using HotelPOS.Application.Users;
using HotelPOS.Contracts.Users;
using HotelPOS.Infrastructure.Persistence;
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

    public async Task InitializeAsync()
    {
        await Fixture.ResetAsync();
        _scope = Fixture.Services.CreateAsyncScope();
    }

    public async Task DisposeAsync() => await _scope.DisposeAsync();

    protected T Get<T>()
        where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    protected async Task<TResult> QueryAsync<TResult>(Func<AppDbContext, Task<TResult>> query)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await query(db);
    }

    protected Task<int> AdminIdAsync() =>
        QueryAsync(db => db.Users.Where(u => u.NormalizedUsername == "ADMIN").Select(u => u.Id).SingleAsync());

    protected async Task<UserDto> CreateUserAsync(string username, string password, params string[] roles)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IUserService>().CreateAsync(new CreateUserRequest
        {
            Username = username,
            DisplayName = username,
            Password = password,
            Roles = roles,
            MustChangePassword = false,
        });
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }
}
