using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Security;

public interface IPermissionService
{
    Task<Result<IReadOnlyList<RolePermissionsDto>>> GetAllRolePermissionsAsync(CancellationToken cancellationToken = default);

    Task<Result<RolePermissionsDto>> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken = default);

    Task<Result> UpdateRolePermissionsAsync(int roleId, UpdateRolePermissionsRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetPermissionsForRolesAsync(IEnumerable<string> roles, CancellationToken cancellationToken = default);
}

