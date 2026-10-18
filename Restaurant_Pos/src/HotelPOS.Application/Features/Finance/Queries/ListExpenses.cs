using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Finance;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Finance.Queries;

public sealed record ListExpensesQuery(ExpenseFilter Filter)
    : IQuery<IReadOnlyList<ExpenseDto>>, IRequirePermission
{
    public string Permission => Permissions.ExpensesView;
}

internal sealed class ListExpensesHandler : IQueryHandler<ListExpensesQuery, IReadOnlyList<ExpenseDto>>
{
    private readonly IAppDbContext _db;

    public ListExpensesHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<ExpenseDto>>> Handle(ListExpensesQuery query, CancellationToken cancellationToken)
    {
        var filter = query.Filter;
        var q = _db.Expenses
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.CashMovement)
            .AsQueryable();

        if (filter.From is { } from)
        {
            q = q.Where(e => e.ExpenseDate >= from);
        }

        if (filter.To is { } to)
        {
            q = q.Where(e => e.ExpenseDate <= to);
        }

        if (filter.CategoryId is { } catId)
        {
            q = q.Where(e => e.CategoryId == catId);
        }

        if (!filter.IncludeVoid)
        {
            q = q.Where(e => !e.IsVoid);
        }

        var expenses = await q
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var userIds = expenses.Select(e => e.CreatedBy).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var userNames = await _db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        var dtos = expenses.Select(e =>
        {
            var userName = e.CreatedBy is { } uid && userNames.TryGetValue(uid, out var name) ? name : null;
            return ExpenseMapping.ToDto(e, userName);
        }).ToList();

        return Result<IReadOnlyList<ExpenseDto>>.Ok(dtos);
    }
}

