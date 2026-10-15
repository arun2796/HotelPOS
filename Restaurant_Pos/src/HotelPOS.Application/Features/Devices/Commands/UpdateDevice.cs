using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Devices.Commands;

// Renames a terminal or binds it to a preparation station (a kitchen screen then shows only that station).
public sealed record UpdateDeviceCommand(Guid Id, UpdateDeviceRequest Input)
    : ICommand, IHasInput<UpdateDeviceRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.DevicesManage;
}

public sealed class UpdateDeviceRequestValidator : AbstractValidator<UpdateDeviceRequest>
{
    public UpdateDeviceRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .Length(DeviceNameRules.MinLength, DeviceNameRules.MaxLength)
            .Matches(DeviceNameRules.Pattern).WithMessage("Device name may contain letters, digits, '-' and '_' only.");
        RuleFor(x => x.StationId).GreaterThan(0).When(x => x.StationId is not null);
    }
}

internal sealed class UpdateDeviceHandler : ICommandHandler<UpdateDeviceCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IOnlineDeviceTracker _onlineTracker;
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;

    public UpdateDeviceHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        IOnlineDeviceTracker onlineTracker,
        IRealtimeNotifier realtime,
        IClock clock)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _onlineTracker = onlineTracker;
        _realtime = realtime;
        _clock = clock;
    }

    public async Task<Result> Handle(UpdateDeviceCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return AppErrors.NotFound("Device", id);
        }

        var normalized = Device.Normalize(request.Name);
        if (await _db.Devices.AnyAsync(d => d.Name == normalized && d.Id != id, cancellationToken))
        {
            return AppErrors.Duplicate($"Another device is already named '{request.Name.Trim()}'.");
        }

        if (request.StationId is { } stationId
            && !await _db.PreparationStations.AnyAsync(s => s.Id == stationId && s.IsActive, cancellationToken))
        {
            return AppErrors.Validation("stationId", $"Preparation station {stationId} does not exist or is inactive.");
        }

        var oldName = device.Name;
        var oldStation = device.StationId;

        device.Rename(request.Name);
        device.AssignStation(request.StationId);

        _audit.Add(AuditActions.DeviceUpdated, nameof(Device), device.Id.ToString(),
            oldValues: new { Name = oldName, StationId = oldStation },
            newValues: new { Name = device.Name, StationId = device.StationId });

        await _db.SaveChangesAsync(cancellationToken);

        _afterCommit.Enqueue(() => _realtime.PublishAsync(
            HubEvents.DeviceStatusChanged,
            new DeviceStatusChangedEvent
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                IsOnline = _onlineTracker.IsDeviceOnline(id),
                IsActive = device.IsActive,
                TimestampUtc = _clock.UtcNow,
            },
            RealtimeAudience.ForRoles(Roles.Admin, Roles.Manager),
            cancellationToken));

        return Result.Success();
    }
}
