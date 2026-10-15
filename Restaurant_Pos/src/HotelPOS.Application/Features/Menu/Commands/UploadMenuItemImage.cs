using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

// Content is null when the upload carried no file.
public sealed record UploadMenuItemImageCommand(int Id, Stream? Content, long Length)
    : ICommand<MenuItemDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

internal sealed class UploadMenuItemImageHandler : ICommandHandler<UploadMenuItemImageCommand, MenuItemDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly MenuItems _items;
    private readonly IMenuImageStore _images;

    public UploadMenuItemImageHandler(IAppDbContext db, IAuditTrail audit, IAfterCommit afterCommit, MenuItems items, IMenuImageStore images)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _items = items;
        _images = images;
    }

    public async Task<Result<MenuItemDto>> Handle(UploadMenuItemImageCommand command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        if (command.Content is null)
        {
            return AppErrors.Validation("file", "Choose a JPEG or PNG picture to upload.");
        }

        if (command.Length <= 0 || command.Length > MenuLimits.MaxImageBytes)
        {
            return AppErrors.Validation("file", $"The picture must be a JPEG or PNG file of at most {MenuLimits.MaxImageBytes / (1024 * 1024)} MB.");
        }

        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", id);
        }

        var fileName = await _images.SaveAsync(id, command.Content, cancellationToken);
        if (fileName is null)
        {
            return AppErrors.Validation("file", "The file is not a readable JPEG or PNG picture.");
        }

        var previous = item.ImagePath;
        item.SetImage(fileName);
        _audit.Add(AuditActions.MenuItemImageChanged, nameof(MenuItem), item.Id.ToString(),
            new { ImagePath = previous }, new { ImagePath = fileName });
        var result = await _items.SaveAsync(id, cancellationToken);

        // Keep exactly one file per item: drop the replaced picture once the change is committed, or the new one
        // right away if the save failed.
        if (result.IsFailure)
        {
            _images.Delete(fileName);
        }
        else if (previous is not null)
        {
            _afterCommit.Enqueue(() =>
            {
                _images.Delete(previous);
                return Task.CompletedTask;
            });
        }

        return result;
    }
}
