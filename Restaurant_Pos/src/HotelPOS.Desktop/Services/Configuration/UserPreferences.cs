using System.IO;
using System.Text.Json;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Realtime;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Configuration;

public interface IUserPreferences
{
    string? LastUsername { get; set; }

    // Per-terminal toast/sound choice for one kind of notification; null until the user changes it (Phase 20.2).
    NotificationPreference? GetNotification(NotificationKind kind);

    void SetNotification(NotificationKind kind, NotificationPreference preference);
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

    public NotificationPreference? GetNotification(NotificationKind kind) =>
        _model.Notifications is { } map && map.TryGetValue(kind.ToString(), out var preference) ? preference : null;

    public void SetNotification(NotificationKind kind, NotificationPreference preference)
    {
        var map = new Dictionary<string, NotificationPreference>(_model.Notifications ?? new(), StringComparer.Ordinal)
        {
            [kind.ToString()] = preference,
        };
        _model = _model with { Notifications = map };
        Persist();
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

        public Dictionary<string, NotificationPreference>? Notifications { get; init; }
    }
}
