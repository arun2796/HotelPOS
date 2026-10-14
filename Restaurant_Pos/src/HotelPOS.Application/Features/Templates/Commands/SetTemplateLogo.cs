using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Administration;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Templates.Commands;

// Replaces (Content set) or removes (Content null and Remove) the logo printed by templates with "Logo" on.
public sealed record SetTemplateLogoCommand(Stream? Content, long Length, bool Remove = false)
    : ICommand, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.SettingsManage;
}

internal sealed class SetTemplateLogoHandler : ICommandHandler<SetTemplateLogoCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ILogoStore _logos;

    public SetTemplateLogoHandler(IAppDbContext db, IAuditTrail audit, IAfterCommit afterCommit, ILogoStore logos)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _logos = logos;
    }

    public async Task<Result> Handle(SetTemplateLogoCommand command, CancellationToken cancellationToken)
    {
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == SettingKeys.LogoFile, cancellationToken);
        if (setting is null)
        {
            setting = new Setting(SettingKeys.LogoFile, string.Empty, Contracts.Enums.SettingDataType.String, "Logo for invoices and PDFs (managed by Print templates).", false);
            _db.Settings.Add(setting);
        }

        var previous = string.IsNullOrWhiteSpace(setting.Value) ? null : setting.Value;
        string? fileName = null;
        if (!command.Remove)
        {
            if (command.Content is null)
            {
                return AppErrors.Validation("file", "Choose a JPEG or PNG picture to upload.");
            }

            if (command.Length <= 0 || command.Length > TemplateLimits.LogoMaxBytes)
            {
                return AppErrors.Validation("file", $"The logo must be a JPEG or PNG file of at most {TemplateLimits.LogoMaxBytes / (1024 * 1024)} MB.");
            }

            fileName = await _logos.SaveAsync(command.Content, cancellationToken);
            if (fileName is null)
            {
                return AppErrors.Validation("file", "The file is not a readable JPEG or PNG picture.");
            }
        }

        setting.SetValue(fileName ?? string.Empty);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.TemplateLogoChanged, nameof(Setting), SettingKeys.LogoFile, new { LogoFile = previous }, new { LogoFile = fileName });
        if (previous is not null)
        {
            _afterCommit.Enqueue(() =>
            {
                _logos.Delete(previous);
                return Task.CompletedTask;
            });
        }

        return Result.Success();
    }
}
