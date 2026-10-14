using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Users.Queries;

public sealed record ListUsersQuery(UserQuery Input) : IQuery<PagedResult<UserDto>>, IHasInput<UserQuery>, IRequirePermission
{
    public string Permission => Permissions.UsersManage;
}

public sealed class UserQueryValidator : AbstractValidator<UserQuery>
{
    public UserQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, UserQuery.MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

internal sealed class ListUsersHandler : IQueryHandler<ListUsersQuery, PagedResult<UserDto>>
{
    private readonly UserAdministration _users;

    public ListUsersHandler(UserAdministration users)
    {
        _users = users;
    }

    public async Task<Result<PagedResult<UserDto>>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        var query = request.Input;
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize);

        var users = _users.UsersWithRoles().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // PostgreSQL compares case-sensitively, so both sides are upper-cased.
            var normalized = query.Search.Trim().ToUpperInvariant();
            users = users.Where(u => u.NormalizedUsername.Contains(normalized) || u.DisplayName.ToUpper().Contains(normalized));
        }

        if (query.IsActive is { } isActive)
        {
            users = users.Where(u => u.IsActive == isActive);
        }

        var total = await users.CountAsync(cancellationToken);
        var items = await users
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<UserDto>
        {
            Items = items.Select(UserMapping.ToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
    }
}
