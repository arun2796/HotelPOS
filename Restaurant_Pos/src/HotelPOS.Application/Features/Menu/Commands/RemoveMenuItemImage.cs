using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

public sealed record RemoveMenuItemImageCommand(int Id) : ICommand<MenuItemDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.MenuManage;
}

internal sealed class RemoveMenuItemImageHandler : ICommandHandler<RemoveMenuItemImageCommand, MenuItemDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly MenuItems _items;
    private readonly IMenuImageStore _images;

    public RemoveMenuItemImageHandler(IAppDbContext db, IAuditTrail audit, IAfterCommit afterCommit, MenuItems items, IMenuImageStore images)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _items = items;
        _images = images;
    }

    public async Task<Result<MenuItemDto>> Handle(RemoveMenuItemImageCommand command, CancellationToken cancellationToken)
    {
        var item = await _db.MenuItems.FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken);
        if (item is null)
        {
            return AppErrors.NotFound("Menu item", command.Id);
        }

        if (item.ImagePath is not { } previous)
        {
            return await _items.GetAsync(command.Id, cancellationToken);
        }

        item.SetImage(null);
        _audit.Add(AuditActions.MenuItemImageChanged, nameof(MenuItem), item.Id.ToString(),
            new { ImagePath = previous }, new { ImagePath = (string?)null });
        var result = await _items.SaveAsync(command.Id, cancellationToken);
        if (result.IsSuccess)
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
