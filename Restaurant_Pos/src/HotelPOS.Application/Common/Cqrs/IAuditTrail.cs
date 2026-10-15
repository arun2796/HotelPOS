using HotelPOS.Application.Common.Interfaces;

namespace HotelPOS.Application.Common.Cqrs;

public sealed record AuditEntry(
    string Action,
    string EntityType,
    string? EntityId,
    object? OldValues,
    object? NewValues,
    AuditActor? Actor);

public interface IAuditTrail
{
    // Values are serialised when the entry is written, after the handler: pass snapshots (anonymous objects),
    // not tracked entities that the handler goes on changing.
    void Add(
        string action,
        string entityType,
        string? entityId,
        object? oldValues = null,
        object? newValues = null,
        AuditActor? actor = null);
}

internal sealed class AuditTrail : IAuditTrail
{
    private readonly List<AuditEntry> _entries = new();

    public void Add(
        string action,
        string entityType,
        string? entityId,
        object? oldValues = null,
        object? newValues = null,
        AuditActor? actor = null) =>
        _entries.Add(new AuditEntry(action, entityType, entityId, oldValues, newValues, actor));

    public IReadOnlyList<AuditEntry> TakeAll()
    {
        var entries = _entries.ToList();
        _entries.Clear();
        return entries;
    }
}
