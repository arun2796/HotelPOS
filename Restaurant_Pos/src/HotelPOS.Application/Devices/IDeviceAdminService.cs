using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Devices;

namespace HotelPOS.Application.Devices;

public interface IDeviceAdminService
{
    Task<Result<IReadOnlyList<DeviceDto>>> GetAllDevicesAsync(CancellationToken cancellationToken = default);

    Task<Result<DeviceDto>> GetDeviceByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> UpdateDeviceAsync(Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeactivateDeviceAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> ActivateDeviceAsync(Guid id, CancellationToken cancellationToken = default);
}

