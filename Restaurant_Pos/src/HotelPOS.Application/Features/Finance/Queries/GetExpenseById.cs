using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Finance;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Finance.Queries;

public sealed record GetExpenseByIdQuery(int Id)
    : IQuery<ExpenseDto>, IRequirePermission
{
    public string Permission => Permissions.ExpensesView;
}

internal sealed class GetExpenseByIdHandler : IQueryHandler<GetExpenseByIdQuery, ExpenseDto>
{
    private readonly IAppDbContext _db;

    public GetExpenseByIdHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<ExpenseDto>> Handle(GetExpenseByIdQuery query, CancellationToken cancellationToken)
    {
        var expense = await _db.Expenses
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.CashMovement)
            .FirstOrDefaultAsync(e => e.Id == query.Id, cancellationToken);

        if (expense is null)
        {
            return AppErrors.NotFound("Expense", query.Id);
        }

        string? userName = null;
        if (expense.CreatedBy is { } uid)
        {
            userName = await _db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken);
        }

        return ExpenseMapping.ToDto(expense, userName);
    }
}

