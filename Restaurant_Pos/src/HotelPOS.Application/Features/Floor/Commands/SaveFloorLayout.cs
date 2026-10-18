using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Floor;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Floor.Commands;

public sealed record SaveFloorLayoutCommand(int SectionId, SaveFloorLayoutRequest Input)
    : ICommand<FloorLayoutDto>, IHasInput<SaveFloorLayoutRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.FloorLayoutManage;
}

public sealed class SaveFloorLayoutRequestValidator : AbstractValidator<SaveFloorLayoutRequest>
{
    public SaveFloorLayoutRequestValidator()
    {
        RuleFor(x => x.Items)
            .NotNull()
            .Must(items => items.Count <= FloorLayoutLimits.MaxItems)
            .WithMessage($"A layout holds at most {FloorLayoutLimits.MaxItems} tables.")
            .Must(items => items.Select(i => i.TableId).Distinct().Count() == items.Count)
            .WithMessage("A table appears more than once.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.TableId).GreaterThan(0);
            item.RuleFor(i => i.PosX).InclusiveBetween(FloorLayoutLimits.MinCoordinate, FloorLayoutLimits.MaxCoordinate);
            item.RuleFor(i => i.PosY).InclusiveBetween(FloorLayoutLimits.MinCoordinate, FloorLayoutLimits.MaxCoordinate);
            item.RuleFor(i => i.Width).InclusiveBetween(FloorLayoutLimits.MinSize, FloorLayoutLimits.MaxSize);
            item.RuleFor(i => i.Height).InclusiveBetween(FloorLayoutLimits.MinSize, FloorLayoutLimits.MaxSize);
            item.RuleFor(i => i.Shape).IsInEnum();
            item.RuleFor(i => i.Rotation).InclusiveBetween(0, 359);
        });
    }
}

internal sealed class SaveFloorLayoutHandler : ICommandHandler<SaveFloorLayoutCommand, FloorLayoutDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IRealtimeNotifier _realtime;

    public SaveFloorLayoutHandler(IAppDbContext db, IClock clock, IAuditTrail audit, IAfterCommit afterCommit, IRealtimeNotifier realtime)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _afterCommit = afterCommit;
        _realtime = realtime;
    }

    public async Task<Result<FloorLayoutDto>> Handle(SaveFloorLayoutCommand command, CancellationToken cancellationToken)
    {
        var (sectionId, input) = (command.SectionId, command.Input);
        if (!await _db.Sections.AnyAsync(s => s.Id == sectionId, cancellationToken))
        {
            return AppErrors.NotFound("Section", sectionId);
        }

        var ids = input.Items.Select(i => i.TableId).ToList();
        var tables = await _db.Tables.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        foreach (var item in input.Items)
        {
            if (!tables.TryGetValue(item.TableId, out var table) || table.SectionId != sectionId)
            {
                return AppErrors.Validation("items", $"Table {item.TableId} does not belong to this section.");
            }

            table.SetLayout(item.PosX, item.PosY, item.Width, item.Height, item.Shape, item.Rotation);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppErrors.Concurrency("table");
        }

        _audit.Add(AuditActions.FloorLayoutSaved, nameof(Section), sectionId.ToString(), null,
            new { SectionId = sectionId, Tables = input.Items.Count });

        _afterCommit.Enqueue(() => _realtime.PublishAsync(HubEvents.FloorLayoutChanged, new FloorLayoutChangedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = sectionId,
            SectionId = sectionId,
        }, RealtimeAudience.All, CancellationToken.None));

        return await FloorLayouts.LoadAsync(_db, sectionId, cancellationToken);
    }
}
