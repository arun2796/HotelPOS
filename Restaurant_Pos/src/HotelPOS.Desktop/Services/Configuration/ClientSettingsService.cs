using System.IO;
using System.Text.Json;
using HotelPOS.Contracts.Common;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Configuration;

public interface IClientSettingsService
{
    ClientSettings Current { get; }

    /// <summary>Path of the file the settings were loaded from or last saved to.</summary>
    string? FilePath { get; }

    event EventHandler? Changed;

    void Save(ClientSettings settings);
}

public sealed class ClientSettingsService : IClientSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly AppPaths _paths;
    private readonly ILogger<ClientSettingsService> _logger;
    private ClientSettings _current;

    public ClientSettingsService(AppPaths paths, ILogger<ClientSettingsService> logger)
    {
        _paths = paths;
        _logger = logger;
        _current = Load();
    }

    public event EventHandler? Changed;

    public ClientSettings Current => _current.Clone();

    public string? FilePath { get; private set; }

    public void Save(ClientSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        try
        {
            Write(_paths.MachineSettingsFile, json);
            FilePath = _paths.MachineSettingsFile;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // ProgramData is writable only after the installer grants access; fall back to the user profile.
            _logger.LogWarning(ex, "Cannot write {Path}; saving settings to the user profile instead", _paths.MachineSettingsFile);
            Write(_paths.UserSettingsFile, json);
            FilePath = _paths.UserSettingsFile;
        }

        _current = settings.Clone();
        _logger.LogInformation("Client settings saved to {Path}: server {ApiBaseUrl}, device {DeviceName} ({DeviceType})",
            FilePath, settings.ApiBaseUrl, settings.DeviceName, settings.DeviceType);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = PosJson.Create();
        options.WriteIndented = true;
        return options;
    }

    private static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    private ClientSettings Load()
    {
        // If both the machine file and the user fallback exist, the most recently saved one wins.
        var candidates = new[] { _paths.MachineSettingsFile, _paths.UserSettingsFile }
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc);

        foreach (var path in candidates)
        {
            try
            {
                var settings = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(path), JsonOptions);
                if (settings is not null)
                {
                    FilePath = path;
                    _logger.LogInformation("Client settings loaded from {Path}", path);
                    return settings;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // A damaged file must not stop the terminal from starting: the first-run screen will appear.
                _logger.LogError(ex, "Client settings file {Path} could not be read", path);
            }
        }

        return new ClientSettings();
    }
}
