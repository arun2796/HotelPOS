using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Licensing;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Api.Tests;

// Own database per class: applying a key or ageing the trial changes the whole server.
public class LicenseTerminalLimitApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public LicenseTerminalLimitApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Trial_ThenKey_ThenTerminalLimit_OverHttp()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);

        var trial = (await (await waiter.GetAsync("/api/license")).ReadEnvelopeAsync<LicenseStatusDto>()).Data!;
        trial.State.Should().Be(LicenseStateNames.Trial);
        trial.MachineId.Should().Be(TestLicense.MachineId);
        trial.ShowWarning.Should().BeTrue();

        (await waiter.PostAsJsonAsync("/api/license/apply", new ApplyLicenseRequest { Key = TestLicense.Issue(2, null) }, PosJson.Options))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var wrongMachine = await admin.PostAsJsonAsync("/api/license/apply", new ApplyLicenseRequest { Key = TestLicense.Issue(2, null, "HP-FFFFFFFFFFFFFFFFFFFF") }, PosJson.Options);
        wrongMachine.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrongMachine.ReadEnvelopeAsync<LicenseStatusDto>()).Errors.Should().Contain(e => e.Code == ErrorCodes.LicenseInvalid);

        var applied = await admin.PostAsJsonAsync("/api/license/apply", new ApplyLicenseRequest { Key = TestLicense.Issue(2, DateOnly.FromDateTime(DateTime.Today).AddDays(365), edition: "Premium") }, PosJson.Options);
        var status = (await applied.ReadEnvelopeAsync<LicenseStatusDto>()).Data!;
        status.State.Should().Be(LicenseStateNames.Licensed);
        status.Edition.Should().Be("Premium");
        status.MaxTerminals.Should().Be(2);
        status.ShowWarning.Should().BeFalse();

        // Two terminals sign in; the third is refused and never registered.
        (await LoginAsync("manager1", "LIC-01")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync("manager1", "LIC-02")).StatusCode.Should().Be(HttpStatusCode.OK);
        var third = await LoginAsync("manager1", "LIC-03");
        third.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await third.ReadEnvelopeAsync<object>()).Errors.Should().Contain(e => e.Code == ErrorCodes.LicenseTerminalLimit);
        (await LoginAsync("manager1", "lic-01")).StatusCode.Should().Be(HttpStatusCode.OK, "a registered terminal can always sign in again");
        var devices = (await (await admin.GetAsync("/api/admin/devices")).ReadEnvelopeAsync<List<HotelPOS.Contracts.Devices.DeviceDto>>()).Data!;
        devices.Should().NotContain(d => d.Name == "LIC-03");
    }

    private async Task<HttpResponseMessage> LoginAsync(string user, string device)
    {
        using var client = _factory.CreateClient();
        return await client.PostAsJsonAsync("/api/auth/login", new HotelPOS.Contracts.Auth.LoginRequest
        {
            Username = user,
            Password = ApiFactory.DemoPassword,
            DeviceName = device,
        }, PosJson.Options);
    }
}

public class LicenseExpiryApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public LicenseExpiryApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExpiredTrial_IsReadOnly_AdminsOnly_UntilAKeyIsApplied()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.ServerLicenses.SingleAsync();
            db.Entry(row).Property(l => l.TrialStartedAt).CurrentValue = DateTime.UtcNow.AddDays(-30);
            await db.SaveChangesAsync();
        }

        var status = (await (await manager.GetAsync("/api/license")).ReadEnvelopeAsync<LicenseStatusDto>()).Data!;
        status.IsExpired.Should().BeTrue();

        // Reads and exports keep working; writes are refused, even with a token issued before the expiry.
        (await manager.GetAsync("/api/menu-items")).StatusCode.Should().Be(HttpStatusCode.OK);
        var write = await admin.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = "Late", SortOrder = 1 }, PosJson.Options);
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await write.ReadEnvelopeAsync<object>()).Errors.Should().Contain(e => e.Code == ErrorCodes.LicenseExpired);

        // Only an administrator can still sign in.
        using var anonymous = _factory.CreateClient();
        var waiterLogin = await anonymous.PostAsJsonAsync("/api/auth/login", new HotelPOS.Contracts.Auth.LoginRequest { Username = "waiter1", Password = ApiFactory.DemoPassword }, PosJson.Options);
        waiterLogin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiterLogin.ReadEnvelopeAsync<object>()).Errors.Should().Contain(e => e.Code == ErrorCodes.LicenseExpired);
        (await anonymous.PostAsJsonAsync("/api/auth/login", new HotelPOS.Contracts.Auth.LoginRequest { Username = "admin", Password = ApiFactory.AdminPassword }, PosJson.Options))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Applying a key lifts it at once.
        (await admin.PostAsJsonAsync("/api/license/apply", new ApplyLicenseRequest { Key = TestLicense.Issue(5, null) }, PosJson.Options)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = "Back", SortOrder = 1 }, PosJson.Options)).StatusCode.Should().Be(HttpStatusCode.Created);
    }
}

public class ImportAndUpdatesApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ImportAndUpdatesApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static MultipartFormDataContent Form(string csv, ImportKind kind = ImportKind.Menu, bool skipInvalid = false)
    {
        var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "menu.csv" },
            { new StringContent(kind.ToString()), "kind" },
            { new StringContent(skipInvalid ? "true" : "false"), "skipInvalid" },
        };
        return content;
    }

    [Fact]
    public async Task Import_PreviewApplyUndo_OverHttp_AsAManager()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        const string csv = "Category,Item,Price\nImported,Imp One,10\nImported,Imp Two,20\nImported,Bad,x\n";

        (await waiter.PostAsync("/api/import/preview", Form(csv))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var preview = (await (await manager.PostAsync("/api/import/preview", Form(csv))).ReadEnvelopeAsync<ImportPreviewDto>()).Data!;
        preview.ValidRows.Should().Be(2);
        preview.ErrorRows.Should().Be(1);
        preview.Errors.Single().Row.Should().Be(4);

        var refused = await manager.PostAsync("/api/import/apply", Form(csv));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
        (await refused.ReadEnvelopeAsync<object>()).Errors.Should().Contain(e => e.Code == ErrorCodes.ValidationError && e.Message.Contains("Row 4"));

        var applied = (await (await manager.PostAsync("/api/import/apply", Form(csv, skipInvalid: true))).ReadEnvelopeAsync<ImportResultDto>()).Data!;
        applied.Skipped.Should().Be(1);
        var last = (await (await manager.GetAsync("/api/import/last")).ReadEnvelopeAsync<ImportBatchDto>()).Data!;
        last.Id.Should().Be(applied.BatchId);
        last.CanUndo.Should().BeTrue();

        var items = (await (await manager.GetAsync("/api/menu-items")).ReadEnvelopeAsync<List<MenuItemDto>>()).Data!;
        items.Select(i => i.Name).Should().Contain(new[] { "Imp One", "Imp Two" });

        (await manager.PostAsync("/api/import/undo", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.PostAsync("/api/import/undo", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
        var after = (await (await manager.GetAsync("/api/menu-items")).ReadEnvelopeAsync<List<MenuItemDto>>()).Data!;
        after.Select(i => i.Name).Should().NotContain("Imp One");

        (await manager.PostAsync("/api/import/preview", new MultipartFormDataContent())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Updates_AreServedAnonymously_AndAdvertisedInSystemInfo()
    {
        using var client = _factory.CreateClient();
        var folder = Path.Combine(_factory.MediaPath, "updates");
        (await client.GetFromJsonAsync<ApiResponse<SystemInfoDto>>("/api/system/info", PosJson.Options))!.Data!.ClientUpdate.Should().BeNull("nothing is published yet");

        await File.WriteAllBytesAsync(Path.Combine(folder, "HotelPOS-Client-Setup-9.9.9.exe"), new byte[] { 1, 2, 3, 4 });
        var manifest = new ClientUpdateInfo { Version = "9.9.9", File = "HotelPOS-Client-Setup-9.9.9.exe", Sha256 = "ABCD", Size = 4 };

        // A manifest without the vendor's signature (a hand-written or tampered latest.json) is never advertised.
        await File.WriteAllTextAsync(Path.Combine(folder, "latest.json"),
            JsonSerializer.Serialize(new { version = "9.9.9", file = "HotelPOS-Client-Setup-9.9.9.exe", sha256 = "ABCD", size = 4 }));
        (await client.GetFromJsonAsync<ApiResponse<SystemInfoDto>>("/api/system/info", PosJson.Options))!.Data!.ClientUpdate.Should().BeNull("the manifest is not signed");

        await File.WriteAllTextAsync(Path.Combine(folder, "latest.json"), TestLicense.SignedManifest(manifest));

        var info = (await client.GetFromJsonAsync<ApiResponse<SystemInfoDto>>("/api/system/info", PosJson.Options))!.Data!;
        info.ClientUpdate!.Version.Should().Be("9.9.9");
        info.ClientUpdate.File.Should().Be("HotelPOS-Client-Setup-9.9.9.exe");
        info.ClientUpdate.Signature.Should().NotBeNullOrEmpty("terminals verify it before downloading");

        var installer = await client.GetAsync("/updates/HotelPOS-Client-Setup-9.9.9.exe");
        installer.StatusCode.Should().Be(HttpStatusCode.OK);
        (await installer.Content.ReadAsByteArrayAsync()).Should().Equal(1, 2, 3, 4);
        (await client.GetAsync("/updates/latest.json")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/updates/..%2fappsettings.json")).StatusCode.Should().NotBe(HttpStatusCode.OK);

        // A manifest pointing at a missing file, or outside the folder, is not advertised even when it is signed.
        await File.WriteAllTextAsync(Path.Combine(folder, "latest.json"),
            TestLicense.SignedManifest(new ClientUpdateInfo { Version = "9.9.9", File = "..\\evil.exe", Sha256 = "AB", Size = 1 }));
        (await client.GetFromJsonAsync<ApiResponse<SystemInfoDto>>("/api/system/info", PosJson.Options))!.Data!.ClientUpdate.Should().BeNull();
    }
}
