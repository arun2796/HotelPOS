using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Devices.Queries;

public sealed record GetDeviceQuery(Guid Id) : IQuery<DeviceDto>, IRequirePermission
{
    public string Permission => Permissions.DevicesManage;
}

internal sealed class GetDeviceHandler : IQueryHandler<GetDeviceQuery, DeviceDto>
{
    private readonly IAppDbContext _db;
    private readonly IOnlineDeviceTracker _onlineTracker;

    public GetDeviceHandler(IAppDbContext db, IOnlineDeviceTracker onlineTracker)
    {
        _db = db;
        _onlineTracker = onlineTracker;
    }

    public async Task<Result<DeviceDto>> Handle(GetDeviceQuery request, CancellationToken cancellationToken)
    {
        var device = await _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken);
        if (device is null)
        {
            return AppErrors.NotFound("Device", request.Id);
        }

        var isOnline = _onlineTracker.IsDeviceOnline(request.Id);
        var stations = await _db.PreparationStations.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        return DeviceRegistry.ToAdminDto(device, isOnline, stations);
    }
}
