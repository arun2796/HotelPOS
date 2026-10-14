using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Queries;

// Settled and voided bills of a business day, optionally searched by table, invoice, bill or order number.
public sealed record ListClosedBillsQuery(ClosedBillQuery Input)
    : IQuery<IReadOnlyList<BillSummaryDto>>, IHasInput<ClosedBillQuery>, IRequirePermission
{
    public string Permission => Permissions.BillingOperate;
}

public sealed class ClosedBillQueryValidator : AbstractValidator<ClosedBillQuery>
{
    public ClosedBillQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(BillingLimits.SearchMaxLength);
    }
}

internal sealed class ListClosedBillsHandler : IQueryHandler<ListClosedBillsQuery, IReadOnlyList<BillSummaryDto>>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly BillSteps _bills;

    public ListClosedBillsHandler(IAppDbContext db, IClock clock, BillSteps bills)
    {
        _db = db;
        _clock = clock;
        _bills = bills;
    }

    public async Task<Result<IReadOnlyList<BillSummaryDto>>> Handle(ListClosedBillsQuery request, CancellationToken cancellationToken)
    {
        var query = request.Input;
        var (from, to) = await _db.BusinessDayAsync(_clock, query.Date, cancellationToken);
        var bills = _db.Bills.AsNoTracking().Where(b =>
            (b.Status == BillStatus.Settled && b.SettledAt >= from && b.SettledAt < to)
            || (b.Status == BillStatus.Voided && b.VoidedAt >= from && b.VoidedAt < to));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpperInvariant();
            var number = int.TryParse(search, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
            bills = bills.Where(b =>
                b.TableCode == search
                || (b.InvoiceNumber != null && b.InvoiceNumber.ToUpper().Contains(search))
                || b.BillNumber == number
                || _db.Orders.Any(o => o.Id == b.OrderId && o.OrderNumber == number));
        }

        return Result.Success(await _bills.SummariesAsync(bills.OrderByDescending(b => b.SettledAt ?? b.VoidedAt), cancellationToken));
    }
}
