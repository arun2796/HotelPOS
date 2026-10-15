using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Devices.Commands;

// Sent by the real-time hub when a device connects or disconnects: records when it was last seen. Not an endpoint.
public sealed record TouchDeviceCommand(Guid DeviceId) : ICommand;

internal sealed class TouchDeviceHandler : ICommandHandler<TouchDeviceCommand>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public TouchDeviceHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result> Handle(TouchDeviceCommand command, CancellationToken cancellationToken)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == command.DeviceId, cancellationToken);
        if (device is null)
        {
            return Result.Success();
        }

        device.Touch(_clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
