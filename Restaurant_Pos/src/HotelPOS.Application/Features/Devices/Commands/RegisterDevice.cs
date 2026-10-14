using HotelPOS.Application.Features.Licensing;
using FluentValidation;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Devices;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Devices.Commands;

// A signed-in terminal registers (or re-registers) itself; open to every signed-in role.
public sealed record RegisterDeviceCommand(RegisterDeviceRequest Input)
    : ICommand<DeviceDto>, IHasInput<RegisterDeviceRequest>, IAuditedCommand;

public sealed class RegisterDeviceRequestValidator : AbstractValidator<RegisterDeviceRequest>
{
    public RegisterDeviceRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .Length(DeviceNameRules.MinLength, DeviceNameRules.MaxLength)
            .Matches(DeviceNameRules.Pattern).WithMessage("Device name may contain letters, digits, '-' and '_' only.");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.MachineName).MaximumLength(100);
        RuleFor(x => x.AppVersion).MaximumLength(30);
        RuleFor(x => x.StationId).GreaterThan(0).When(x => x.StationId is not null);
    }
}

internal sealed class RegisterDeviceHandler : ICommandHandler<RegisterDeviceCommand, DeviceDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly DeviceRegistry _devices;
    private readonly ILicenseService _licenses;

    public RegisterDeviceHandler(IAppDbContext db, IClock clock, DeviceRegistry devices, ILicenseService licenses)
    {
        _db = db;
        _clock = clock;
        _devices = devices;
        _licenses = licenses;
    }

    public async Task<Result<DeviceDto>> Handle(RegisterDeviceCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        if (request.StationId is { } stationId
            && !await _db.PreparationStations.AnyAsync(s => s.Id == stationId && s.IsActive, cancellationToken))
        {
            return AppErrors.Validation("stationId", $"Preparation station {stationId} does not exist or is inactive.");
        }

        if (await _licenses.CheckTerminalAsync(request.Name, cancellationToken) is { } limit)
        {
            return limit;
        }

        var device = await _devices.UpsertAsync(request.Name, request.Type, request.MachineName, request.AppVersion, request.StationId, cancellationToken);
        if (!device.IsActive)
        {
            return AppErrors.DeviceDisabled(device.Name);
        }

        device.Touch(_clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return DeviceRegistry.ToRegistrationDto(device);
    }
}
