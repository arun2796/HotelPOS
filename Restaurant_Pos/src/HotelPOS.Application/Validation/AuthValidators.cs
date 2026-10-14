using FluentValidation;
using HotelPOS.Contracts.Devices;

namespace HotelPOS.Application.Validation;

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
