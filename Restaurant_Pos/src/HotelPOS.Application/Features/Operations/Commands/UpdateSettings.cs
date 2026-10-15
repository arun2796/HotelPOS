using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Administration;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Operations.Commands;

// Saves the changed settings and answers with all of them.
public sealed record UpdateSettingsCommand(UpdateSettingsRequest Input)
    : ICommand<IReadOnlyList<SettingDto>>, IHasInput<UpdateSettingsRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.SettingsManage;
}

public sealed class UpdateSettingsRequestValidator : AbstractValidator<UpdateSettingsRequest>
{
    public UpdateSettingsRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Key).NotEmpty().MaximumLength(100);
            item.RuleFor(i => i.Value).NotNull().MaximumLength(4000);
        });
    }
}

internal sealed class UpdateSettingsHandler : ICommandHandler<UpdateSettingsCommand, IReadOnlyList<SettingDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public UpdateSettingsHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<IReadOnlyList<SettingDto>>> Handle(UpdateSettingsCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var keys = request.Items.Select(i => i.Key).ToList();
        var settings = await _db.Settings.Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, cancellationToken);

        var errors = new List<ApiError>();
        foreach (var item in request.Items)
        {
            if (!settings.TryGetValue(item.Key, out var setting))
            {
                errors.Add(new ApiError(ErrorCodes.ValidationError, $"Unknown setting '{item.Key}'.", item.Key));
            }
            else if (setting.Key == SettingKeys.MenuVersion && setting.Value != item.Value.Trim())
            {
                // Clients compare this number to decide whether their cached menu is current.
                errors.Add(new ApiError(ErrorCodes.ValidationError, "The menu version is managed by the system.", item.Key));
            }
            else if (!Setting.IsValidValue(setting.DataType, item.Value.Trim()))
            {
                errors.Add(new ApiError(ErrorCodes.ValidationError, $"'{item.Value}' is not a valid {setting.DataType} value.", item.Key));
            }
            else if (setting.Key == SettingKeys.PasswordMinLength
                && int.Parse(item.Value.Trim(), System.Globalization.CultureInfo.InvariantCulture) is var length
                && (length < PasswordPolicy.MinLength || length > PasswordPolicy.MaxLength))
            {
                errors.Add(new ApiError(ErrorCodes.ValidationError, $"The minimum password length must be between {PasswordPolicy.MinLength} and {PasswordPolicy.MaxLength}.", item.Key));
            }
        }

        if (errors.Count > 0)
        {
            return AppErrors.Validation("One or more settings are invalid.", errors);
        }

        var oldValues = new Dictionary<string, string>();
        var newValues = new Dictionary<string, string>();
        foreach (var item in request.Items)
        {
            var setting = settings[item.Key];
            if (setting.Value == item.Value.Trim())
            {
                continue;
            }

            oldValues[setting.Key] = setting.Value;
            setting.SetValue(item.Value);
            newValues[setting.Key] = setting.Value;
        }

        if (newValues.Count > 0)
        {
            _audit.Add(AuditActions.SettingsUpdated, nameof(Setting), null, oldValues, newValues);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var all = await _db.Settings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(cancellationToken);
        return all.Select(SettingMapping.ToDto).ToList();
    }
}
