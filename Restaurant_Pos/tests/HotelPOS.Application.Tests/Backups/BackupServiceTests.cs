using System.IO.Compression;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Administration;
using HotelPOS.Infrastructure.Media;
using HotelPOS.Infrastructure.Persistence;
using HotelPOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HotelPOS.Application.Tests.Backups;

// Real pg_dump / pg_restore against the throw-away test database. Machines without the PostgreSQL client tools
// return early (the server tests already need a PostgreSQL server, so the tools are normally there).
public class BackupServiceTests : DatabaseTestBase, IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hotelpos-backup-tests-" + Guid.NewGuid().ToString("N"));

    public BackupServiceTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    private static bool ToolsMissing => PostgresTools.Find("pg_dump", null) is null || PostgresTools.Find("pg_restore", null) is null;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Run_WritesAVerifiedDump_ZipsThePictures_AndAudits()
    {
        if (ToolsMissing)
        {
            return;
        }

        await UseFolderAsync();
        var pictures = Get<IOptions<MediaOptions>>().Value.MenuImagesPath;
        Directory.CreateDirectory(pictures);
        await File.WriteAllBytesAsync(Path.Combine(pictures, "7-abc.jpg"), new byte[] { 1, 2, 3 });
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Contracts.Security.Roles.Admin);

        var result = await Get<IBackupService>().RunAsync(BackupTrigger.Manual);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        var backup = result.Value;
        backup.Status.Should().Be(BackupStatus.Succeeded);
        backup.TriggeredBy.Should().Be("admin");
        File.Exists(Path.Combine(_folder, backup.FileName)).Should().BeTrue();
        using (var zip = ZipFile.OpenRead(Path.Combine(_folder, backup.MediaFileName!)))
        {
            zip.Entries.Select(e => e.Name).Should().Contain("7-abc.jpg");
        }

        backup.SizeBytes.Should().BeGreaterThan(0);
        (await QueryAsync(db => db.AuditLogs.CountAsync(a => a.Action == AuditActions.BackupCompleted))).Should().Be(1);
    }

    [Fact]
    public async Task Run_DeletesBackupsOlderThanTheRetention()
    {
        if (ToolsMissing)
        {
            return;
        }

        await UseFolderAsync();
        Directory.CreateDirectory(_folder);
        var oldFile = Path.Combine(_folder, "hotelpos-old.dump");
        await File.WriteAllTextAsync(oldFile, "old");
        await QueryAsync(async db =>
        {
            var old = new Backup("hotelpos-old.dump", BackupTrigger.Schedule, "schedule", Clock.UtcNow.AddDays(-30));
            old.Succeed(3, null, Clock.UtcNow.AddDays(-30));
            db.Backups.Add(old);
            return await db.SaveChangesAsync();
        });

        var result = await Get<IBackupService>().RunAsync(BackupTrigger.Manual);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        File.Exists(oldFile).Should().BeFalse();
        (await QueryAsync(db => db.Backups.Select(b => b.FileName).ToListAsync())).Should().Equal(result.Value.FileName);
    }

    [Fact]
    public async Task Run_WithoutTheTools_FailsCleanly_AndAuditsTheFailure()
    {
        await UseFolderAsync();
        await using var scope = Fixture.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var service = new BackupService(
            provider.GetRequiredService<AppDbContext>(),
            provider.GetRequiredService<IAuditService>(),
            CurrentUser,
            Clock,
            Options.Create(new BackupOptions { PgBinPath = Path.Combine(_folder, "no-such-bin") }),
            provider.GetRequiredService<IOptions<MediaOptions>>(),
            NullLogger<BackupService>.Instance);

        var result = await service.RunAsync(BackupTrigger.Manual);

        result.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Message.Should().Contain("pg_dump was not found");
        var row = await QueryAsync(db => db.Backups.SingleAsync());
        row.Status.Should().Be(BackupStatus.Failed);
        (Directory.Exists(_folder) ? Directory.EnumerateFiles(_folder) : Enumerable.Empty<string>())
            .Should().BeEmpty("no partial files may remain");
        (await QueryAsync(db => db.AuditLogs.CountAsync(a => a.Action == AuditActions.BackupFailed))).Should().Be(1);
        (await service.GetOverviewAsync()).LastFailure!.FileName.Should().Be(row.FileName);
    }

    [Fact]
    public async Task Scheduler_RunsOncePerDay_AfterTheBackupTime()
    {
        if (ToolsMissing)
        {
            return;
        }

        await UseFolderAsync();
        // The test clock starts at 06:30 UTC; a backup time just before that (in local time) is already due.
        var dueLocal = TimeZoneInfo.ConvertTimeFromUtc(Clock.UtcNow.AddMinutes(-5), TimeZoneInfo.Local);
        await SetAsync(SettingKeys.BackupTime, dueLocal.ToString("HH:mm"));
        var scheduler = new BackupHostedService(
            Fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BackupOptions()),
            NullLogger<BackupHostedService>.Instance);

        await scheduler.RunIfDueAsync(CancellationToken.None);
        await scheduler.RunIfDueAsync(CancellationToken.None);

        var backups = await QueryAsync(db => db.Backups.ToListAsync());
        backups.Should().ContainSingle().Which.Trigger.Should().Be(BackupTrigger.Schedule);
    }

    private Task UseFolderAsync() => SetAsync(SettingKeys.BackupPath, _folder);

    private Task SetAsync(string key, string value) =>
        QueryAsync(async db =>
        {
            (await db.Settings.SingleAsync(s => s.Key == key)).SetValue(value);
            return await db.SaveChangesAsync();
        });
}
