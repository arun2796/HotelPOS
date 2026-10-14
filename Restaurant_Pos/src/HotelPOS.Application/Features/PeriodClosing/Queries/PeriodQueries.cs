using System.Globalization;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.PeriodClosing.Commands;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Gst;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.PeriodClosing.Queries;

// The twelve months of a financial year and the year itself, with their close status. Default: the current year.
public sealed record GetPeriodsQuery(int? FinancialYear) : IQuery<IReadOnlyList<PeriodDto>>, IRequirePermission
{
    public string Permission => Permissions.GstView;
}

public sealed record GetPeriodPrecheckQuery(PeriodKind Kind, string Period) : IQuery<PeriodPrecheckDto>, IRequirePermission
{
    public string Permission => Permissions.PeriodClose;
}

internal sealed class GetPeriodsHandler : IQueryHandler<GetPeriodsQuery, IReadOnlyList<PeriodDto>>
{
    private readonly IAppDbContext _db;
    private readonly IBusinessDayLock _days;
    private readonly PeriodChecks _checks;

    public GetPeriodsHandler(IAppDbContext db, IBusinessDayLock days, PeriodChecks checks)
    {
        _db = db;
        _days = days;
        _checks = checks;
    }

    public async Task<Result<IReadOnlyList<PeriodDto>>> Handle(GetPeriodsQuery request, CancellationToken cancellationToken)
    {
        var year = request.FinancialYear ?? Periods.FinancialYearOf(await _days.TodayAsync(cancellationToken));
        if (!Periods.TryYear(year.ToString(CultureInfo.InvariantCulture), out var start, out var end))
        {
            return AppErrors.Validation("financialYear", "Choose a financial year by its first year, for example 2026.");
        }

        var closes = await _db.PeriodCloses.AsNoTracking().Where(p => p.PeriodStart >= start && p.PeriodEnd <= end).ToListAsync(cancellationToken);
        var known = (await _checks.ToDtosAsync(closes, cancellationToken)).ToDictionary(p => (p.Kind, p.Period));
        var rows = new List<PeriodDto>();
        for (var month = start; month <= end; month = month.AddMonths(1))
        {
            var key = Periods.Key(PeriodKind.Month, month);
            rows.Add(known.GetValueOrDefault((PeriodKind.Month, key)) ?? PeriodChecks.Open(PeriodKind.Month, month, month.AddMonths(1).AddDays(-1)));
        }

        var yearKey = Periods.Key(PeriodKind.Year, start);
        rows.Add(known.GetValueOrDefault((PeriodKind.Year, yearKey)) ?? PeriodChecks.Open(PeriodKind.Year, start, end));
        return Result<IReadOnlyList<PeriodDto>>.Ok(rows);
    }
}

internal sealed class GetPeriodPrecheckHandler : IQueryHandler<GetPeriodPrecheckQuery, PeriodPrecheckDto>
{
    private readonly PeriodChecks _checks;

    public GetPeriodPrecheckHandler(PeriodChecks checks)
    {
        _checks = checks;
    }

    public async Task<Result<PeriodPrecheckDto>> Handle(GetPeriodPrecheckQuery request, CancellationToken cancellationToken)
    {
        var range = PeriodRequests.Parse(request.Kind, request.Period);
        if (range.IsFailure)
        {
            return range.Error!;
        }

        return await _checks.PrecheckAsync(request.Kind, range.Value.Start, range.Value.End, cancellationToken);
    }
}
