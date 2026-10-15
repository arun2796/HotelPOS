using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Security;
using HotelPOS.Contracts.Auth;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Auth.Queries;

// The signed-in user's profile and current permissions (read fresh, not from the token).
public sealed record GetCurrentUserQuery : IQuery<CurrentUserDto>;

internal sealed class GetCurrentUserHandler : IQueryHandler<GetCurrentUserQuery, CurrentUserDto>
{
    private readonly ICurrentUser _currentUser;
    private readonly RolePermissions _permissions;
    private readonly AuthTokens _tokens;

    public GetCurrentUserHandler(ICurrentUser currentUser, RolePermissions permissions, AuthTokens tokens)
    {
        _currentUser = currentUser;
        _permissions = permissions;
        _tokens = tokens;
    }

    public async Task<Result<CurrentUserDto>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return AppErrors.Unauthenticated();
        }

        var user = await _tokens.UsersWithRoles().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return AppErrors.Unauthenticated();
        }

        var permissions = await _permissions.ForRolesAsync(user.RoleNames, cancellationToken);
        return AuthTokens.ToCurrentUser(user, permissions);
    }
}
