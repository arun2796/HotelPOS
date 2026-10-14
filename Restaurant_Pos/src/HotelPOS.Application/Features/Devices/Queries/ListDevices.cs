using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Devices.Queries;

public sealed record ListDevicesQuery : IQuery<IReadOnlyList<DeviceDto>>, IRequirePermission
{
    public string Permission => Permissions.DevicesManage;
}

internal sealed class ListDevicesHandler : IQueryHandler<ListDevicesQuery, IReadOnlyList<DeviceDto>>
{
    private readonly IAppDbContext _db;
    private readonly IOnlineDeviceTracker _onlineTracker;

    public ListDevicesHandler(IAppDbContext db, IOnlineDeviceTracker onlineTracker)
    {
        _db = db;
        _onlineTracker = onlineTracker;
    }

    public async Task<Result<IReadOnlyList<DeviceDto>>> Handle(ListDevicesQuery request, CancellationToken cancellationToken)
    {
        var devices = await _db.Devices.AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        var onlineIds = _onlineTracker.OnlineDeviceIds();
        var stations = await _db.PreparationStations.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        return devices.Select(d => DeviceRegistry.ToAdminDto(d, onlineIds.Contains(d.Id), stations)).ToList();
    }
}
