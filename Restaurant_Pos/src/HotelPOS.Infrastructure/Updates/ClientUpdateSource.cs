using System.Text.Json;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using Microsoft.Extensions.Options;

namespace HotelPOS.Infrastructure.Updates;

public sealed class UpdateOptions
{
    public const string SectionName = "Updates";

    /// <summary>Folder served as /updates/: the client installer and latest.json (publish.ps1 fills it).</summary>
    public string Path { get; set; } = string.Empty;

    public string Folder => string.IsNullOrWhiteSpace(Path) ? System.IO.Path.Combine(AppContext.BaseDirectory, "updates") : Path;
}

// Reads /updates/latest.json = { "version", "file", "sha256", "size" }; null when none was published or it is not
// consistent with an installer file next to it.
public sealed class ClientUpdateSource : IClientUpdateSource
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly UpdateOptions _options;

    public ClientUpdateSource(IOptions<UpdateOptions> options)
    {
        _options = options.Value;
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

            var info = JsonSerializer.Deserialize<ClientUpdateInfo>(File.ReadAllText(manifest), Json);
            if (info is null || string.IsNullOrWhiteSpace(info.Version) || string.IsNullOrWhiteSpace(info.File)
                || info.File != System.IO.Path.GetFileName(info.File)
                || !File.Exists(System.IO.Path.Combine(_options.Folder, info.File)))
            {
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
