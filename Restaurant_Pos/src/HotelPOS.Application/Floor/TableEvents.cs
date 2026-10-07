using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Domain.Floor;

namespace HotelPOS.Application.Floor;

public sealed class TableEvents
{
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;

    public TableEvents(IRealtimeNotifier realtime, IClock clock)
    {
        _realtime = realtime;
        _clock = clock;
    }

    // Call after the commit. Not tied to the request: a client that hung up must not stop others from being notified.
    public Task PublishStatusAsync(Table table, int? orderNumber = null) =>
        _realtime.PublishAsync(HubEvents.TableStatusChanged, new TableStatusChangedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = table.Id,
            EntityVersion = RowVersions.Encode(table.RowVersion),
            TableId = table.Id,
            TableCode = table.Code,
            Status = table.Status,
            GuestCount = table.GuestCount,
            OccupiedAtUtc = table.OccupiedAt,
            OrderId = table.CurrentOrderId,
            OrderNumber = table.CurrentOrderId is null ? null : orderNumber ?? table.CurrentOrder?.OrderNumber,
        }, RealtimeAudience.All, CancellationToken.None);
}
