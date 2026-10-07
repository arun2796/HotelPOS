using System.Text.Json;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Common;
using HotelPOS.Domain.Administration;

namespace HotelPOS.Infrastructure.Services;

public sealed class AuditService : IAuditService
{
    private const int MaxJsonLength = 8000;

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public AuditService(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public void Record(
        string action,
        string entityType,
        string? entityId,
        object? oldValues = null,
        object? newValues = null,
        AuditActor? actor = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = Serialize(oldValues),
            NewValues = Serialize(newValues),
            UserId = actor?.UserId ?? _currentUser.UserId,
            UserName = Truncate(actor?.UserName ?? _currentUser.UserName, 50),
            DeviceId = _currentUser.DeviceId,
            DeviceName = _currentUser.DeviceName,
            MachineName = _currentUser.MachineName,
            IpAddress = _currentUser.IpAddress,
            CorrelationId = _currentUser.CorrelationId,
            Timestamp = _clock.UtcNow,
        });
    }

    private static string? Serialize(object? values)
    {
        if (values is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(values, PosJson.Options);
        return json.Length <= MaxJsonLength ? json : json[..MaxJsonLength];
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
