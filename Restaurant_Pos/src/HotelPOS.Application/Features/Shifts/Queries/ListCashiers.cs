using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Security;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Shifts.Queries;

// Active users who may operate a cash shift: the people a shift can be handed over to.
public sealed record ListCashiersQuery : IQuery<IReadOnlyList<CashierDto>>, IRequirePermission
{
    public string Permission => Permissions.ShiftOperate;
}

internal sealed class ListCashiersHandler : IQueryHandler<ListCashiersQuery, IReadOnlyList<CashierDto>>
{
    private readonly IAppDbContext _db;
    private readonly RolePermissions _permissions;

    public ListCashiersHandler(IAppDbContext db, RolePermissions permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<Result<IReadOnlyList<CashierDto>>> Handle(ListCashiersQuery request, CancellationToken cancellationToken)
    {
        var users = await _db.Users.AsNoTracking().Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(cancellationToken);
        var openShifts = await _db.CashShifts.AsNoTracking()
            .Where(s => s.Status == CashShiftStatus.Open)
            .Select(s => new { s.CashierUserId, s.DeviceId })
            .ToListAsync(cancellationToken);
        var deviceIds = openShifts.Select(s => s.DeviceId).ToList();
        var devices = await _db.Devices.AsNoTracking().Where(d => deviceIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var cashiers = new List<CashierDto>();
        foreach (var user in users)
        {
            var granted = await _permissions.ForRolesAsync(user.RoleNames, cancellationToken);
            if (user.HasRole(Roles.Admin) || granted.Contains(Permissions.ShiftOperate, StringComparer.Ordinal))
            {
                cashiers.Add(new CashierDto
                {
                    UserId = user.Id,
                    Username = user.Username,
                    DisplayName = user.DisplayName,
                    OpenShiftDevice = openShifts.FirstOrDefault(s => s.CashierUserId == user.Id) is { } open
                        ? devices.GetValueOrDefault(open.DeviceId) ?? ShiftSteps.UnknownDevice(open.DeviceId)
                        : null,
                });
            }
        }

        return cashiers;
    }
}
