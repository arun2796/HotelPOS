using System.IO;
using System.Text.Json;
using HotelPOS.Contracts.Common;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Configuration;

public interface IUserPreferences
{
    string? LastUsername { get; set; }
}

public sealed class UserPreferences : IUserPreferences
{
    private readonly string _path;
    private readonly ILogger<UserPreferences> _logger;
    private Model _model;

    public UserPreferences(AppPaths paths, ILogger<UserPreferences> logger)
    {
        _path = Path.Combine(paths.UserDataDirectory, "preferences.json");
        _logger = logger;
        _model = Load();
    }

    public string? LastUsername
    {
        get => _model.LastUsername;
        set
        {
            _model = _model with { LastUsername = value };
            Persist();
        }
    }

    private Model Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<Model>(File.ReadAllText(_path), PosJson.Options) ?? new Model()
                : new Model();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read preferences {Path}", _path);
            return new Model();
        }
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_model, PosJson.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save preferences {Path}", _path);
        }
    }

    private sealed record Model
    {
        public string? LastUsername { get; init; }
    }
}
