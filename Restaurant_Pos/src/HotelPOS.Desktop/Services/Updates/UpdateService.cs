using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Security.Cryptography;
using HotelPOS.Contracts.Admin;
using HotelPOS.Desktop.Modules.Auth;
using HotelPOS.Desktop.Services.Configuration;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Updates;

public sealed record UpdateResult(bool Success, string? Message = null);

public interface IUpdateService
{
    // The installer the server offers when it is newer than this terminal, else null.
    ClientUpdateInfo? OfferFor(ClientUpdateInfo? latest);

    // Checks the vendor's signature on the manifest, downloads from the server's /updates/, verifies the SHA-256 and
    // starts the installer. On success the caller exits the application so the installer can replace its files.
    Task<UpdateResult> DownloadAndStartAsync(ClientUpdateInfo update, IProgress<double>? progress, CancellationToken cancellationToken);
}

public interface IInstallerLauncher
{
    void Start(string installerPath);
}

// UAC elevation comes from the installer's own manifest (PrivilegesRequired=admin), hence ShellExecute.
public sealed class InstallerLauncher : IInstallerLauncher
{
    // /RESTART=1: client.iss starts the new version for the signed-in user when the silent install is done.
    public const string Arguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NOCANCEL /RESTART=1";

    public void Start(string installerPath) =>
        Process.Start(new ProcessStartInfo(installerPath, Arguments) { UseShellExecute = true });
}

public sealed class UpdateService : IUpdateService
{
    public const string HttpClientName = "HotelPOS.Updates";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClientSettingsService _settings;
    private readonly IInstallerLauncher _launcher;
    private readonly ILogger<UpdateService> _logger;
    private readonly string _folder;
    private readonly string _vendorPublicKey;

    public UpdateService(IHttpClientFactory httpClientFactory, IClientSettingsService settings, IInstallerLauncher launcher, ILogger<UpdateService> logger)
        : this(httpClientFactory, settings, launcher, logger, Path.Combine(Path.GetTempPath(), "HotelPOS-update"))
    {
    }

    // vendorPublicKey: the tests sign with their own key pair; the application always uses the embedded vendor key.
    public UpdateService(IHttpClientFactory httpClientFactory, IClientSettingsService settings, IInstallerLauncher launcher, ILogger<UpdateService> logger, string folder, string? vendorPublicKey = null)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _launcher = launcher;
        _logger = logger;
        _folder = folder;
        _vendorPublicKey = vendorPublicKey ?? VendorKeys.LicensePublicKey;
    }

    public ClientUpdateInfo? OfferFor(ClientUpdateInfo? latest) => UpdateChecker.Offer(AppInfo.Version, latest);

    public async Task<UpdateResult> DownloadAndStartAsync(ClientUpdateInfo update, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!UpdateChecker.IsSafeInstallerName(update.File) || string.IsNullOrWhiteSpace(update.Sha256))
        {
            return new UpdateResult(false, "The server's update information is incomplete. Ask your administrator to publish the update again.");
        }

        // The installer runs with administrator rights, so only a manifest the vendor signed is acted on: whoever
        // answers on the server's port cannot make this terminal download and run a file of their choosing.
        if (!UpdateManifestSignature.Verify(update, _vendorPublicKey))
        {
            _logger.LogError("The update manifest for {Version} is not signed by the vendor; the update was refused", update.Version);
            return new UpdateResult(false, "The update offered by the server is not signed by the HotelPOS vendor, so it was not installed. Ask your administrator to publish the release again with publish.ps1.");
        }

        if (!Uri.TryCreate(_settings.Current.ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            return new UpdateResult(false, "The server address is not configured.");
        }

        var target = Path.Combine(_folder, update.File);
        try
        {
            Directory.CreateDirectory(_folder);
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(new Uri(baseUri, "updates/" + Uri.EscapeDataString(update.File)), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateResult(false, $"The server could not provide the update (HTTP {(int)response.StatusCode}).");
            }

            var total = response.Content.Headers.ContentLength ?? update.Size;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    done += read;
                    if (total > 0)
                    {
                        progress?.Report(Math.Min(1d, (double)done / total));
                    }
                }
            }

            if (!UpdateChecker.HashMatches(Convert.ToHexString(hash.GetHashAndReset()), update.Sha256))
            {
                File.Delete(target);
                _logger.LogError("The downloaded installer {File} does not match its checksum", update.File);
                return new UpdateResult(false, "The downloaded update is damaged (checksum mismatch). Nothing was installed; try again.");
            }

            _launcher.Start(target);
            _logger.LogInformation("Started the installer for HotelPOS {Version}", update.Version);
            return new UpdateResult(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            // Win32Exception: the user declined the administrator prompt.
            _logger.LogWarning(ex, "Updating to {Version} failed", update.Version);
            return new UpdateResult(false, ex is System.ComponentModel.Win32Exception
                ? "The update was not started (administrator permission is needed to install it)."
                : "The update could not be downloaded. Check the connection to the server and try again.");
        }
    }
}

// The decisions, without any I/O.
public static class UpdateChecker
{
    public static ClientUpdateInfo? Offer(string installedVersion, ClientUpdateInfo? latest) =>
        latest is not null
        && !string.IsNullOrWhiteSpace(latest.File)
        && VersionGate.IsNewer(latest.Version, installedVersion)
            ? latest
            : null;

    // Only a plain file name of an installer; the server's manifest must not point the terminal elsewhere.
    public static bool IsSafeInstallerName(string? file) =>
        !string.IsNullOrWhiteSpace(file)
        && file == Path.GetFileName(file)
        && file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !file.Contains("..", StringComparison.Ordinal);

    public static bool HashMatches(string actualHex, string expectedHex) =>
        string.Equals(actualHex.Trim(), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
}
