using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Domain.Gst;

namespace HotelPOS.Application.Features.PeriodClosing;

// Every terminal hears about a period close or reopen; handlers queue this on IAfterCommit.
internal sealed class PeriodEvents
{
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;

    public PeriodEvents(IRealtimeNotifier realtime, IClock clock)
    {
        _realtime = realtime;
        _clock = clock;
    }

    public Task PublishAsync(PeriodClose close, bool closed, string changedBy) =>
        _realtime.PublishAsync(HubEvents.PeriodStatusChanged, new PeriodStatusChangedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = close.PeriodStart.DayNumber,
            Kind = close.Kind,
            Period = Periods.Key(close.Kind, close.PeriodStart),
            Label = Periods.Label(close.Kind, close.PeriodStart),
            From = close.PeriodStart,
            To = close.PeriodEnd,
            Closed = closed,
            ChangedBy = changedBy,
            Reason = closed ? null : close.ReopenReason,
        }, RealtimeAudience.All);
}
