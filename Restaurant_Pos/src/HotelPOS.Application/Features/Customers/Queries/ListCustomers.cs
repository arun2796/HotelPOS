using FluentValidation;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Customers;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Customers.Queries;

// The customer list and the bill's picker: by name, phone digits or GSTIN, a page at a time.
public sealed record ListCustomersQuery(CustomerQuery Input) : IQuery<PagedResult<CustomerDto>>, IHasInput<CustomerQuery>, IRequirePermission
{
    public string Permission => Permissions.BillingOperate;
}

public sealed class CustomerQueryValidator : AbstractValidator<CustomerQuery>
{
    public CustomerQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PagedQuery.MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(CustomerLimits.SearchMaxLength);
    }
}

internal sealed class ListCustomersHandler : IQueryHandler<ListCustomersQuery, PagedResult<CustomerDto>>
{
    private readonly IAppDbContext _db;

    public ListCustomersHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<PagedResult<CustomerDto>>> Handle(ListCustomersQuery request, CancellationToken cancellationToken)
    {
        var query = request.Input;
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize);

        var customers = _db.Customers.AsNoTracking();
        if (!query.IncludeInactive)
        {
            customers = customers.Where(c => c.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // PostgreSQL compares case-sensitively, so both sides are upper-cased. Digits search the stored 10-digit phone.
            var text = query.Search.Trim().ToUpperInvariant();
            var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
            var phone = digits.Length >= 3 ? Customer.NormalizePhone(digits) : null;
            customers = customers.Where(c => c.Name.ToUpper().Contains(text)
                || (c.LegalName != null && c.LegalName.ToUpper().Contains(text))
                || (c.Gstin != null && c.Gstin.Contains(text))
                || (phone != null && c.Phone != null && c.Phone.Contains(phone)));
        }

        var total = await customers.CountAsync(cancellationToken);
        var items = await customers.OrderBy(c => c.Name).ThenBy(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<CustomerDto>
        {
            Items = items.Select(CustomerMapping.ToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
    }
}
