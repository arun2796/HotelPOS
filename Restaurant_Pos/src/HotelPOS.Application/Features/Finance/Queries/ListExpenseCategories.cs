using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Finance;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Finance.Queries;

public sealed record ListExpenseCategoriesQuery(bool IncludeInactive = false)
    : IQuery<IReadOnlyList<ExpenseCategoryDto>>, IRequirePermission
{
    public string Permission => Permissions.ExpensesView;
}

internal sealed class ListExpenseCategoriesHandler : IQueryHandler<ListExpenseCategoriesQuery, IReadOnlyList<ExpenseCategoryDto>>
{
    private readonly IAppDbContext _db;

    public ListExpenseCategoriesHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<ExpenseCategoryDto>>> Handle(ListExpenseCategoriesQuery query, CancellationToken cancellationToken)
    {
        var q = _db.ExpenseCategories.AsNoTracking();
        if (!query.IncludeInactive)
        {
            q = q.Where(c => c.IsActive);
        }

        var list = await q.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<ExpenseCategoryDto>>.Ok(list.Select(ExpenseMapping.ToDto).ToList());
    }
}

