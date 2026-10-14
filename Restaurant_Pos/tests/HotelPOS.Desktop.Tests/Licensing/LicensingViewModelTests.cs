using System.IO;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using HotelPOS.Contracts.Admin;
using HotelPOS.Desktop.Modules.Licensing;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Services.Updates;
using HotelPOS.Desktop.Shell;
using HotelPOS.Desktop.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Licensing;

public sealed class UpdateCheckerTests
{
    private static ClientUpdateInfo Update(string version, string file = "HotelPOS-Client-Setup-9.exe") =>
        new() { Version = version, File = file, Sha256 = "AB", Size = 3 };

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("1.0.0-beta+abc", "0.9.9", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.0.9", "0.1.0", false)]
    [InlineData("garbage", "0.1.0", false)]
    [InlineData("0.2.0", "garbage", false)]
    public void Offer_IsMadeOnlyForAHigherVersion(string latest, string installed, bool offered)
    {
        (UpdateChecker.Offer(installed, Update(latest)) is not null).Should().Be(offered);
    }

    [Fact]
    public void NoManifest_OrNoFile_MeansNoOffer()
    {
        UpdateChecker.Offer("0.1.0", null).Should().BeNull();
        UpdateChecker.Offer("0.1.0", Update("0.2.0", file: "")).Should().BeNull();
    }

    [Theory]
    [InlineData("HotelPOS-Client-Setup-0.2.0.exe", true)]
    [InlineData("..\\evil.exe", false)]
    [InlineData("sub/evil.exe", false)]
    [InlineData("setup.zip", false)]
    [InlineData("", false)]
    public void OnlyAPlainInstallerName_IsAccepted(string file, bool safe) =>
        UpdateChecker.IsSafeInstallerName(file).Should().Be(safe);

    [Fact]
    public void Checksums_AreComparedIgnoringCase() =>
        UpdateChecker.HashMatches("abcdef", " ABCDEF ").Should().BeTrue();
}

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hotelpos-update-test-" + Guid.NewGuid().ToString("N"));
    private readonly IInstallerLauncher _launcher = Substitute.For<IInstallerLauncher>();
    private readonly byte[] _installer = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        public byte[] Body { get; init; } = Array.Empty<byte>();

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Body) });
        }
    }

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private UpdateService Service(FakeHandler handler) => new(
        new FakeFactory(handler),
        new InMemorySettings(new ClientSettings { ApiBaseUrl = "http://192.168.1.10:5000" }),
        _launcher,
        NullLogger<UpdateService>.Instance,
        _folder);

    private ClientUpdateInfo Info(string? sha = null) => new()
    {
        Version = "9.9.9",
        File = "HotelPOS-Client-Setup-9.9.9.exe",
        Sha256 = sha ?? Convert.ToHexString(SHA256.HashData(_installer)),
        Size = _installer.Length,
    };

    [Fact]
    public async Task Download_WithAMatchingChecksum_StartsTheInstaller()
    {
        var handler = new FakeHandler { Body = _installer };
        var progress = new List<double>();

        var result = await Service(handler).DownloadAndStartAsync(Info(), new Progress<double>(progress.Add), CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        handler.Requested!.ToString().Should().Be("http://192.168.1.10:5000/updates/HotelPOS-Client-Setup-9.9.9.exe");
        _launcher.Received(1).Start(Path.Combine(_folder, "HotelPOS-Client-Setup-9.9.9.exe"));
        File.ReadAllBytes(Path.Combine(_folder, "HotelPOS-Client-Setup-9.9.9.exe")).Should().Equal(_installer);
    }

    [Fact]
    public async Task Download_WithAWrongChecksum_DeletesTheFile_AndNeverRunsIt()
    {
        var result = await Service(new FakeHandler { Body = _installer }).DownloadAndStartAsync(Info(sha: "00FF"), null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("damaged");
        _launcher.DidNotReceiveWithAnyArgs().Start(default!);
        File.Exists(Path.Combine(_folder, "HotelPOS-Client-Setup-9.9.9.exe")).Should().BeFalse();
    }

    [Fact]
    public async Task ServerErrors_AndUnsafeManifests_AreReported_NotRun()
    {
        var notFound = await Service(new FakeHandler { Status = HttpStatusCode.NotFound }).DownloadAndStartAsync(Info(), null, CancellationToken.None);
        var unsafeName = await Service(new FakeHandler { Body = _installer }).DownloadAndStartAsync(Info() with { File = "..\\x.exe" }, null, CancellationToken.None);
        var noChecksum = await Service(new FakeHandler { Body = _installer }).DownloadAndStartAsync(Info() with { Sha256 = "" }, null, CancellationToken.None);

        notFound.Success.Should().BeFalse();
        unsafeName.Success.Should().BeFalse();
        noChecksum.Success.Should().BeFalse();
        _launcher.DidNotReceiveWithAnyArgs().Start(default!);
    }
}

public sealed class StatusBannerTests
{
    [Fact]
    public void Trial_ExpiryNear_AndExpired_AreWorded_AndAHealthyLicenceIsSilent()
    {
        StatusBannerViewModel.Describe(new LicenseStatusDto { State = LicenseStateNames.Trial, DaysRemaining = 5, ShowWarning = true })
            .Should().Be(("Trial version: 5 days left. An administrator must apply a licence key before it ends.", false));
        StatusBannerViewModel.Describe(new LicenseStatusDto { State = LicenseStateNames.Licensed, DaysRemaining = 1, ExpiresOn = new DateOnly(2026, 12, 31), ShowWarning = true })
            .Text.Should().StartWith("The licence expires in 1 day, on ");
        var expired = StatusBannerViewModel.Describe(new LicenseStatusDto { State = LicenseStateNames.Expired, IsExpired = true, ShowWarning = true, Message = "The licence expired on 01-10-2026." });
        expired.Critical.Should().BeTrue();
        expired.Text.Should().Contain("read-only");
        StatusBannerViewModel.Describe(new LicenseStatusDto { State = LicenseStateNames.Licensed, DaysRemaining = 200, ShowWarning = false }).Text.Should().BeEmpty();
        StatusBannerViewModel.Describe(null).Text.Should().BeEmpty();
    }
}

public sealed class LicenseAndImportViewModelTests
{
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    [Fact]
    public async Task ApplyingAKey_ShowsTheNewStatus_AndClearsTheBox()
    {
        var api = Substitute.For<ILicenseApi>();
        api.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<LicenseStatusDto>.Ok(new LicenseStatusDto { State = LicenseStateNames.Trial, DaysRemaining = 6, MachineId = "HP-1" }));
        api.ApplyAsync("HPOS1-abc.def", Arg.Any<CancellationToken>()).Returns(ApiResult<LicenseStatusDto>.Ok(new LicenseStatusDto
        {
            State = LicenseStateNames.Licensed, Edition = "Standard", MaxTerminals = 5, Customer = "Hotel A", MachineId = "HP-1",
        }));
        var vm = new LicenseViewModel(api, _notifications);
        await vm.OnNavigatedToAsync(null);
        vm.StateText.Should().Be("Trial: 6 day(s) left");
        vm.ApplyKeyCommand.CanExecute(null).Should().BeFalse("the box is empty");

        vm.KeyText = "  HPOS1-abc.def \n";
        await vm.ApplyKeyCommand.ExecuteAsync(null);

        vm.StateText.Should().Be("Licensed");
        vm.CustomerText.Should().Be("Hotel A");
        vm.ExpiryText.Should().Contain("perpetual");
        vm.KeyText.Should().BeEmpty();
        _notifications.Received().Success(Arg.Is<string>(m => m.Contains("5 terminal")));
    }

    [Fact]
    public async Task ARefusedKey_ShowsTheServersReason_AndKeepsTheKeyForEditing()
    {
        var api = Substitute.For<ILicenseApi>();
        api.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<LicenseStatusDto>.Ok(new LicenseStatusDto { MachineId = "HP-1" }));
        api.ApplyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<LicenseStatusDto>.Fail("LICENSE_INVALID", "This licence was issued for another PC."));
        var vm = new LicenseViewModel(api, _notifications) { KeyText = "HPOS1-x.y" };

        await vm.ApplyKeyCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("another PC");
        vm.KeyText.Should().Be("HPOS1-x.y");
    }

    private ImportPreviewDto Preview(int errors) => new()
    {
        Kind = ImportKind.Menu,
        FileName = "menu.csv",
        Headers = new[] { "Cat", "Dish", "Rate" },
        Fields = new[]
        {
            new ImportFieldDto { Key = "category", Label = "Category", Required = true },
            new ImportFieldDto { Key = "item", Label = "Item name", Required = true },
            new ImportFieldDto { Key = "price", Label = "Price", Required = true },
            new ImportFieldDto { Key = "tax", Label = "Tax" },
        },
        Mapping = new Dictionary<string, string> { ["category"] = "Cat", ["item"] = "Dish" },
        TotalRows = 10,
        ValidRows = 10 - errors,
        ErrorRows = errors,
        ToCreate = 10 - errors,
        Errors = Enumerable.Range(0, errors).Select(i => new ImportRowErrorDto { Row = i + 2, Field = "Price", Message = "bad" }).ToList(),
    };

    [Fact]
    public async Task ImportWizard_WalksFileColumnsPreviewApplyUndo()
    {
        var file = Path.Combine(Path.GetTempPath(), "hotelpos-wizard-" + Guid.NewGuid().ToString("N") + ".csv");
        await File.WriteAllTextAsync(file, "Cat,Dish,Rate\nA,B,1\n");
        try
        {
            var api = Substitute.For<IImportApi>();
            var files = Substitute.For<IFilePicker>();
            files.PickImportFile().Returns(file);
            _dialogs.ConfirmAsync(default!, default!, default!, default!, default).ReturnsForAnyArgs(true);
            api.GetLastAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<ImportBatchDto>.Ok(new ImportBatchDto { Id = 1, FileName = "old.csv", Status = ImportStatus.Applied, CanUndo = true, AppliedAtUtc = DateTime.UtcNow }));
            api.PreviewAsync(Arg.Any<ImportKind>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
                .Returns(ApiResult<ImportPreviewDto>.Ok(Preview(errors: 2)));
            api.ApplyAsync(Arg.Any<ImportKind>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(ApiResult<ImportResultDto>.Ok(new ImportResultDto { BatchId = 2, Created = 7, Updated = 1, Skipped = 2 }));
            api.UndoAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<ImportBatchDto>.Ok(new ImportBatchDto { Id = 2, Status = ImportStatus.Undone }));
            var vm = new ImportWizardViewModel(api, files, _notifications, _dialogs);
            await vm.OnNavigatedToAsync(null);
            vm.CanUndo.Should().BeTrue();

            await vm.ChooseFileCommand.ExecuteAsync(null);

            vm.Step.Should().Be(ImportStep.Columns);
            vm.Mapping.Select(m => m.Field.Key).Should().Equal("category", "item", "price", "tax");
            vm.Mapping.Single(m => m.Field.Key == "category").SelectedHeader.Should().Be("Cat");
            vm.Mapping.Single(m => m.Field.Key == "price").SelectedHeader.Should().Be(FieldMappingRow.NotInFile);

            // A required column that is not chosen stops the step.
            await vm.ShowPreviewCommand.ExecuteAsync(null);
            vm.Step.Should().Be(ImportStep.Columns);
            vm.ErrorMessage.Should().Contain("Price");

            vm.Mapping.Single(m => m.Field.Key == "price").SelectedHeader = "Rate";
            await vm.ShowPreviewCommand.ExecuteAsync(null);
            await api.Received().PreviewAsync(ImportKind.Menu, Arg.Any<Stream>(), Arg.Any<string>(),
                Arg.Is<IReadOnlyDictionary<string, string>?>(m => m!["price"] == "Rate" && !m.ContainsKey("tax")), Arg.Any<CancellationToken>());

            vm.Step.Should().Be(ImportStep.Preview);
            vm.Errors.Should().HaveCount(2);
            vm.Summary.Should().Contain("8 ready").And.Contain("2 with errors");
            vm.CanApply.Should().BeFalse("errors need fixing or skipping");
            vm.SkipInvalid = true;
            vm.CanApply.Should().BeTrue();

            await vm.ApplyCommand.ExecuteAsync(null);

            vm.Step.Should().Be(ImportStep.Done);
            vm.ResultText.Should().Be("Imported: 7 added, 1 updated, 2 rows skipped.");
            await api.Received(1).ApplyAsync(ImportKind.Menu, Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), true, Arg.Any<CancellationToken>());

            await vm.UndoCommand.ExecuteAsync(null);
            await api.Received(1).UndoAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(file);
        }
    }
}
