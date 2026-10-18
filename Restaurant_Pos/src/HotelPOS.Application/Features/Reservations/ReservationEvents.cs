using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Domain.Floor;

namespace HotelPOS.Application.Features.Reservations;

public sealed class ReservationEvents
{
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;

    public ReservationEvents(IRealtimeNotifier realtime, IClock clock)
    {
        _realtime = realtime;
        _clock = clock;
    }

    public Task PublishChangedAsync(Reservation reservation, string tableCode) =>
        _realtime.PublishAsync(HubEvents.ReservationChanged, new ReservationChangedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = reservation.Id,
            ReservationId = reservation.Id,
            TableId = reservation.TableId,
            TableCode = tableCode,
            Status = reservation.Status,
            StartTimeUtc = reservation.StartTimeUtc,
            EndTimeUtc = reservation.EndTimeUtc,
        }, RealtimeAudience.All, CancellationToken.None);
}

