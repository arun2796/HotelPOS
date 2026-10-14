using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.DayEnd.Queries;

// Step 1 and 2 of the wizard: what still blocks the day and the day's totals. Day null takes the default day.
public sealed record GetDayEndPrecheckQuery(DateOnly? Day) : IQuery<DayEndPrecheckDto>, IRequirePermission
{
    public string Permission => Permissions.DayEndRun;
}

internal sealed class GetDayEndPrecheckHandler : IQueryHandler<GetDayEndPrecheckQuery, DayEndPrecheckDto>
{
    private readonly DayEndChecks _checks;

    public GetDayEndPrecheckHandler(DayEndChecks checks)
    {
        _checks = checks;
    }

    public async Task<Result<DayEndPrecheckDto>> Handle(GetDayEndPrecheckQuery request, CancellationToken cancellationToken)
    {
        var defaultDay = await _checks.DefaultDayAsync(cancellationToken);
        var day = request.Day ?? defaultDay;
        return new DayEndPrecheckDto
        {
            BusinessDay = day,
            DefaultDay = defaultDay,
            Status = await _checks.StatusAsync(day, cancellationToken),
            BlockingItems = await _checks.BlockingAsync(day, cancellationToken),
            Summary = await _checks.SummaryAsync(day, cancellationToken),
        };
    }
}
