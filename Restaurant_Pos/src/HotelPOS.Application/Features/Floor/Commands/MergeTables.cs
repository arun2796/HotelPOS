using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Floor;

namespace HotelPOS.Application.Features.Floor.Commands;

// The captain pushes a second table against the party's table: the companion locks (nobody can open an order on it)
// and everything the party orders goes on the primary table's order and bill.
public sealed record MergeTablesCommand(int PrimaryTableId, MergeTablesRequest Input)
    : ICommand<TableDto>, IHasInput<MergeTablesRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.TablesOperate;
}

public sealed class MergeTablesRequestValidator : AbstractValidator<MergeTablesRequest>
{
    public MergeTablesRequestValidator()
    {
        RuleFor(x => x.CompanionTableId).GreaterThan(0);
    }
}

internal sealed class MergeTablesHandler : ICommandHandler<MergeTablesCommand, TableDto>
{
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IClock _clock;
    private readonly FloorTables _tables;
    private readonly TableEvents _events;

    public MergeTablesHandler(IAuditTrail audit, IAfterCommit afterCommit, IClock clock, FloorTables tables, TableEvents events)
    {
        _audit = audit;
        _afterCommit = afterCommit;
        _clock = clock;
        _tables = tables;
        _events = events;
    }

    public async Task<Result<TableDto>> Handle(MergeTablesCommand command, CancellationToken cancellationToken)
    {
        var primary = await _tables.LoadAsync(command.PrimaryTableId, cancellationToken);
        if (primary is null)
        {
            return AppErrors.NotFound("Table", command.PrimaryTableId);
        }

        var companion = await _tables.LoadAsync(command.Input.CompanionTableId, cancellationToken);
        if (companion is null)
        {
            return AppErrors.NotFound("Table", command.Input.CompanionTableId);
        }

        try
        {
            primary.Merge(companion, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return AppErrors.BusinessRule(ex.Message);
        }

        _audit.Add(AuditActions.TableMerged, nameof(Table), primary.Id.ToString(),
            newValues: new { Primary = primary.Code, Companion = companion.Code });
        var result = await _tables.SaveAsync(primary, primary.Section!.Name, publish: true, cancellationToken);
        if (result.IsSuccess)
        {
            _afterCommit.Enqueue(() => _events.PublishStatusAsync(companion));
        }

        return result;
    }
}

// Takes a companion table back out of its party (a merge made by mistake, or the extra table is no longer needed).
public sealed record UnmergeTableCommand(int CompanionTableId) : ICommand<TableDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.TablesOperate;
}

internal sealed class UnmergeTableHandler : ICommandHandler<UnmergeTableCommand, TableDto>
{
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly FloorTables _tables;
    private readonly TableEvents _events;

    public UnmergeTableHandler(IAuditTrail audit, IAfterCommit afterCommit, FloorTables tables, TableEvents events)
    {
        _audit = audit;
        _afterCommit = afterCommit;
        _tables = tables;
        _events = events;
    }

    public async Task<Result<TableDto>> Handle(UnmergeTableCommand command, CancellationToken cancellationToken)
    {
        var companion = await _tables.LoadAsync(command.CompanionTableId, cancellationToken);
        if (companion is null)
        {
            return AppErrors.NotFound("Table", command.CompanionTableId);
        }

        if (companion.MergedIntoTableId is not { } primaryId)
        {
            return AppErrors.BusinessRule($"Table {companion.Code} is not merged with another table.");
        }

        // Tracked next to the companion, so Unmerge also takes it out of the primary table's list.
        var primary = await _tables.LoadAsync(primaryId, cancellationToken);
        var primaryCode = companion.MergedInto?.Code ?? string.Empty;
        companion.Unmerge();
        _audit.Add(AuditActions.TableUnmerged, nameof(Table), companion.Id.ToString(),
            newValues: new { Primary = primaryCode, Companion = companion.Code });
        var result = await _tables.SaveAsync(companion, companion.Section!.Name, publish: true, cancellationToken);
        if (result.IsSuccess && primary is not null)
        {
            _afterCommit.Enqueue(() => _events.PublishStatusAsync(primary));
        }

        return result;
    }
}
