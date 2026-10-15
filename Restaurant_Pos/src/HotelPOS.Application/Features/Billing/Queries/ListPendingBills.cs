using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Queries;

// The cashier's queue: requested and finalized bills.
public sealed record ListPendingBillsQuery : IQuery<IReadOnlyList<BillSummaryDto>>, IRequirePermission
{
    public string Permission => Permissions.BillingOperate;
}

internal sealed class ListPendingBillsHandler : IQueryHandler<ListPendingBillsQuery, IReadOnlyList<BillSummaryDto>>
{
    private readonly IAppDbContext _db;
    private readonly BillSteps _bills;

    public ListPendingBillsHandler(IAppDbContext db, BillSteps bills)
    {
        _db = db;
        _bills = bills;
    }

    public async Task<Result<IReadOnlyList<BillSummaryDto>>> Handle(ListPendingBillsQuery request, CancellationToken cancellationToken) =>
        Result.Success(await _bills.SummariesAsync(
            _db.Bills.AsNoTracking()
                .Where(b => b.Status == BillStatus.Open || b.Status == BillStatus.Finalized)
                .OrderBy(b => b.CreatedAt),
            cancellationToken));
}
