using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Common;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Infrastructure;
using HotelPOS.Infrastructure.Persistence;
using HotelPOS.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HotelPOS.Api.Tests;

public class OperationsApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public OperationsApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Startup_WithAnUnreachableDatabase_FailsWithAClearMessage_WithoutThePassword()
    {
        // Built in code: a literal connection string with a password would (rightly) trip StaticSecurityChecksTests.
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = 1,
            Database = "hotelpos_nowhere",
            Username = "pos",
            Password = "very-secret",
        }.ConnectionString;
        await using var services = InfrastructureOnly(connectionString, autoMigrate: true);

        var act = () => services.InitializeDatabaseAsync();

        var error = (await act.Should().ThrowAsync<StartupCheckException>()).Which;
        error.Message.Should().Contain("'hotelpos_nowhere' on 127.0.0.1:1").And.Contain("PostgreSQL service is running")
            .And.NotContain("very-secret");
    }

    [Fact]
    public async Task Startup_WithPendingMigrations_AndAutoMigrateOff_RefusesToStart()
    {
        await using var services = InfrastructureOnly(TestPostgres.NewDatabase("startupcheck"), autoMigrate: false);

        var act = () => services.InitializeDatabaseAsync();

        (await act.Should().ThrowAsync<StartupCheckException>()).Which.Message
            .Should().Contain("migration").And.Contain("Database:AutoMigrate is false");
    }

    [Fact]
    public async Task Health_ReportsDegraded_WhenDiskSpaceIsBelowTheThreshold()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Monitoring:MinFreeDiskMb"] = "999999999" })));

        var response = await factory.CreateClient().GetAsync("/health");

        // Degraded still answers 200: the API serves orders, but the technician must free space.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Degraded\"").And.Contain("\"disk\":\"Degraded\"").And.Contain("Low disk space");
    }

    [Fact]
    public async Task LogTail_ReturnsTheNewestEntries_FilteredByLevel()
    {
        // Its own log folder: other test hosts write their logs to the shared one at the same time.
        var logDirectory = Path.Combine(Path.GetTempPath(), "hotelpos-logtail-" + Guid.NewGuid().ToString("N"));
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:Directory"] = logDirectory })));
        using var anonymous = factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = ApiFactory.AdminPassword });
        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new("Bearer", (await login.ReadEnvelopeAsync<Contracts.Auth.LoginResponse>()).Data!.AccessToken);
        await anonymous.PostAsJsonAsync("/api/auth/login", new { username = "nobody", password = "wrong-password" });

        var all = (await (await admin.GetAsync("/api/admin/logs?lines=50")).ReadEnvelopeAsync<LogTailDto>()).Data!;
        var warnings = (await (await admin.GetAsync("/api/admin/logs?lines=50&level=WRN")).ReadEnvelopeAsync<LogTailDto>()).Data!;
        var invalid = await admin.GetAsync("/api/admin/logs?lines=0");

        all.FileName.Should().StartWith("api-");
        all.Lines.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(50);
        warnings.Lines.Should().NotBeEmpty().And.OnlyContain(l => l.Contains("[WRN]"));
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ServerNotice_ReachesEveryTerminal_AndIsAudited()
    {
        var waiterLogin = await _factory.LoginAsync("waiter1", ApiFactory.DemoPassword);
        await using var waiterHub = _factory.CreateHubConnection(waiterLogin.AccessToken);
        var received = new TaskCompletionSource<ServerNoticeEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiterHub.On<ServerNoticeEvent>(HubEvents.ServerNotice, n => received.TrySetResult(n));
        await waiterHub.StartAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);

        var empty = await admin.PostAsJsonAsync("/api/admin/notice", new ServerNoticeRequest { Message = " " }, PosJson.Options);
        var sent = await admin.PostAsJsonAsync("/api/admin/notice",
            new ServerNoticeRequest { Level = NoticeLevel.Warning, Message = "Server restarting in 2 minutes for maintenance" }, PosJson.Options);

        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        sent.StatusCode.Should().Be(HttpStatusCode.OK);
        var notice = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        notice.Level.Should().Be(NoticeLevel.Warning);
        notice.Message.Should().Be("Server restarting in 2 minutes for maintenance");
        notice.SentBy.Should().Be("admin");
        var audit = (await (await admin.GetAsync($"/api/admin/audit-logs?action={AuditActions.ServerNoticeSent}")).ReadEnvelopeAsync<PagedResult<AuditLogDto>>()).Data!;
        audit.Items.Should().ContainSingle(a => a.NewValues!.Contains("maintenance"));
    }

    [Fact]
    public async Task BackupNow_WritesAVerifiedBackup_ShownInTheOverview()
    {
        if (Infrastructure.Services.PostgresTools.Find("pg_dump", null) is null)
        {
            return;
        }

        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);

        var run = await admin.PostAsync("/api/admin/backup", null);
        var overview = (await (await admin.GetAsync("/api/admin/backups")).ReadEnvelopeAsync<BackupOverviewDto>()).Data!;

        run.StatusCode.Should().Be(HttpStatusCode.OK);
        var backup = (await run.ReadEnvelopeAsync<BackupDto>()).Data!;
        backup.Status.Should().Be(Contracts.Enums.BackupStatus.Succeeded);
        File.Exists(Path.Combine(overview.Folder, backup.FileName)).Should().BeTrue();
        overview.Backups.Should().Contain(b => b.Id == backup.Id);
        overview.LastFailure.Should().BeNull();
    }

    // The database initializer outside any web host, so a failing check surfaces as an exception instead of a
    // process exit.
    private static ServiceProvider InfrastructureOnly(string connectionString, bool autoMigrate)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:HotelPOS"] = connectionString,
            ["Database:AutoMigrate"] = autoMigrate.ToString(),
            ["Database:StartupWaitSeconds"] = "0",
        }).Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddHttpContextAccessor()
            .AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
