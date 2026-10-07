using FluentValidation;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Devices;

namespace HotelPOS.Application.Validation;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
        RuleFor(x => x.DeviceName!)
            .Length(DeviceNameRules.MinLength, DeviceNameRules.MaxLength)
            .Matches(DeviceNameRules.Pattern).WithMessage("Device name may contain letters, digits, '-' and '_' only.")
            .When(x => !string.IsNullOrWhiteSpace(x.DeviceName));
        RuleFor(x => x.DeviceType).IsInEnum().When(x => x.DeviceType is not null);
        RuleFor(x => x.MachineName).MaximumLength(100);
        RuleFor(x => x.AppVersion).MaximumLength(30);
    }
}

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
    }
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).ValidPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must be different from the current one.");
    }
}

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

internal static class PasswordRuleExtensions
{
    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(PasswordPolicy.MinLength)
            .MaximumLength(PasswordPolicy.MaxLength);
}
