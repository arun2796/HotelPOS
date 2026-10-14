using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;

namespace HotelPOS.Application.Features.Billing.Queries;

// The cashier desk and waiters: the endpoint gates by role, there is no permission for it. A waiter only sees the
// bills of their own orders (BillSteps.GetAsync).
public sealed record GetBillQuery(int Id) : IQuery<BillDetailDto>;

internal sealed class GetBillHandler : IQueryHandler<GetBillQuery, BillDetailDto>
{
    private readonly BillSteps _bills;

    public GetBillHandler(BillSteps bills)
    {
        _bills = bills;
    }

    public Task<Result<BillDetailDto>> Handle(GetBillQuery request, CancellationToken cancellationToken) =>
        _bills.GetAsync(request.Id, cancellationToken);
}
