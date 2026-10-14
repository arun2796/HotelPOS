using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Users.Queries;

public sealed record GetUserQuery(int Id) : IQuery<UserDto>, IRequirePermission
{
    public string Permission => Permissions.UsersManage;
}

internal sealed class GetUserHandler : IQueryHandler<GetUserQuery, UserDto>
{
    private readonly UserAdministration _users;

    public GetUserHandler(UserAdministration users)
    {
        _users = users;
    }

    public async Task<Result<UserDto>> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        var user = await _users.UsersWithRoles().AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
        return user is null ? AppErrors.NotFound("User", request.Id) : UserMapping.ToDto(user);
    }
}
