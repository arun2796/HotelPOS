using System.Text.Json;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HotelPOS.Infrastructure.Updates;

public sealed class UpdateOptions
{
    public const string SectionName = "Updates";

    /// <summary>Folder served as /updates/: the client installer and latest.json (publish.ps1 fills it).</summary>
    public string Path { get; set; } = string.Empty;

    public string Folder => string.IsNullOrWhiteSpace(Path) ? System.IO.Path.Combine(AppContext.BaseDirectory, "updates") : Path;
}

// Reads /updates/latest.json = { "version", "file", "sha256", "size", "signature" }; null when none was published, it is
// not consistent with an installer file next to it, or the vendor's signature over it is missing or wrong (the
// terminals would refuse it anyway; not advertising it keeps a tampered folder from showing "Update now").
public sealed class ClientUpdateSource : IClientUpdateSource
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly UpdateOptions _options;
    private readonly ILicenseTrust _trust;
    private readonly ILogger<ClientUpdateSource> _logger;
    private string? _lastRejected;

    public ClientUpdateSource(IOptions<UpdateOptions> options, ILicenseTrust trust, ILogger<ClientUpdateSource> logger)
    {
        _options = options.Value;
        _trust = trust;
        _logger = logger;
    }

    public ClientUpdateInfo? Latest()
    {
        try
        {
            var manifest = System.IO.Path.Combine(_options.Folder, "latest.json");
            if (!File.Exists(manifest))
            {
                return null;
            }

            var text = File.ReadAllText(manifest);
            var info = JsonSerializer.Deserialize<ClientUpdateInfo>(text, Json);
            if (info is null || string.IsNullOrWhiteSpace(info.Version) || string.IsNullOrWhiteSpace(info.File)
                || info.File != System.IO.Path.GetFileName(info.File)
                || !File.Exists(System.IO.Path.Combine(_options.Folder, info.File)))
            {
                return null;
            }

            if (!UpdateManifestSignature.Verify(info, _trust.PublicKey))
            {
                // Once per manifest content, not on every /api/system/info call.
                if (!string.Equals(_lastRejected, text, StringComparison.Ordinal))
                {
                    _lastRejected = text;
                    _logger.LogWarning(
                        "updates/latest.json for {Version} is not signed by the vendor (publish it with build/publish.ps1, which signs it); no update is offered",
                        info.Version);
                }

                return null;
            }

            return info;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
