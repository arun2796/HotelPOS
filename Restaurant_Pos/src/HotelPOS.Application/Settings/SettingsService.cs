using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Domain.Administration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HotelPOS.Application.Settings;

public interface ISettingsService
{
    Task<IReadOnlyList<SettingDto>> GetPublicAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SettingDto>>> UpdateAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Anonymous information used by clients to verify the server address ("Test connection").</summary>
public interface ISystemInfoService
{
    Task<SystemInfoDto> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class SettingsService : ISettingsService, ISystemInfoService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly AppOptions _app;

    public SettingsService(IAppDbContext db, IAuditService audit, IClock clock, IOptions<AppOptions> app)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _app = app.Value;
    }

    public async Task<IReadOnlyList<SettingDto>> GetPublicAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _db.Settings.AsNoTracking().Where(s => s.IsPublic).OrderBy(s => s.Key).ToListAsync(cancellationToken);
        return settings.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<SettingDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _db.Settings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(cancellationToken);
        return settings.Select(ToDto).ToList();
    }

    public async Task<Result<IReadOnlyList<SettingDto>>> UpdateAsync(UpdateSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var keys = request.Items.Select(i => i.Key).ToList();
        var settings = await _db.Settings.Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, cancellationToken);

        var errors = new List<ApiError>();
        foreach (var item in request.Items)
        {
            if (!settings.TryGetValue(item.Key, out var setting))
            {
                errors.Add(new ApiError(ErrorCodes.ValidationError, $"Unknown setting '{item.Key}'.", item.Key));
            }
            else if (setting.Key == SettingKeys.MenuVersion && setting.Value != item.Value.Trim())
            {
                // Clients compare this number to decide whether their cached menu is current.
                errors.Add(new ApiError(ErrorCodes.ValidationError, "The menu version is managed by the system.", item.Key));
            }
            else if (!Setting.IsValidValue(setting.DataType, item.Value.Trim()))
            {
                errors.Add(new ApiError(ErrorCodes.ValidationError, $"'{item.Value}' is not a valid {setting.DataType} value.", item.Key));
            }
        }

        if (errors.Count > 0)
        {
            return AppErrors.Validation("One or more settings are invalid.", errors);
        }

        var oldValues = new Dictionary<string, string>();
        var newValues = new Dictionary<string, string>();
        foreach (var item in request.Items)
        {
            var setting = settings[item.Key];
            if (setting.Value == item.Value.Trim())
            {
                continue;
            }

            oldValues[setting.Key] = setting.Value;
            setting.SetValue(item.Value);
            newValues[setting.Key] = setting.Value;
        }

        if (newValues.Count > 0)
        {
            _audit.Record(AuditActions.SettingsUpdated, nameof(Setting), null, oldValues, newValues);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result<IReadOnlyList<SettingDto>>.Ok(await GetAllAsync(cancellationToken));
    }

    public async Task<SystemInfoDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var name = await _db.Settings.AsNoTracking()
            .Where(s => s.Key == SettingKeys.RestaurantName)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return new SystemInfoDto
        {
            RestaurantName = name ?? "HotelPOS",
            ApiVersion = _app.ApiVersion,
            MinClientVersion = _app.MinClientVersion,
            ServerTimeUtc = _clock.UtcNow,
        };
    }

    private static SettingDto ToDto(Setting s) => new()
    {
        Key = s.Key,
        Value = s.Value,
        DataType = s.DataType,
        Description = s.Description,
        IsPublic = s.IsPublic,
    };
}
