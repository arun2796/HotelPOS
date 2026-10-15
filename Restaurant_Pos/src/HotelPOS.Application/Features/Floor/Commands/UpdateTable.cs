using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Floor;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Floor.Commands;

public sealed record UpdateTableCommand(int Id, UpdateTableRequest Input)
    : ICommand<TableDto>, IHasInput<UpdateTableRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.TablesManage;
}

public sealed class UpdateTableRequestValidator : AbstractValidator<UpdateTableRequest>
{
    public UpdateTableRequestValidator()
    {
        RuleFor(x => x.Code).ValidTableCode();
        RuleFor(x => x.Name).MaximumLength(FloorLimits.NameMaxLength);
        RuleFor(x => x.SectionId).GreaterThan(0).WithMessage("Select a section.");
        RuleFor(x => x.Capacity).InclusiveBetween(FloorLimits.MinCapacity, FloorLimits.MaxCapacity);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

internal sealed class UpdateTableHandler : ICommandHandler<UpdateTableCommand, TableDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly FloorTables _tables;

    public UpdateTableHandler(IAppDbContext db, IAuditTrail audit, FloorTables tables)
    {
        _db = db;
        _audit = audit;
        _tables = tables;
    }

    public async Task<Result<TableDto>> Handle(UpdateTableCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var table = await _tables.LoadAsync(id, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", id);
        }

        if (!RowVersions.Matches(table.RowVersion, request.RowVersion))
        {
            return AppErrors.Concurrency("table", TableMapping.ToDto(table));
        }

        var section = table.SectionId == request.SectionId
            ? table.Section!
            : await _db.Sections.FirstOrDefaultAsync(s => s.Id == request.SectionId, cancellationToken);
        if (section is null)
        {
            return AppErrors.Validation("sectionId", "The selected section does not exist.");
        }

        var code = Table.NormalizeCode(request.Code);
        if (await _db.Tables.AnyAsync(t => t.Code == code && t.Id != id, cancellationToken))
        {
            return AppErrors.Duplicate($"Table code '{code}' is already used.");
        }

        var willBeActive = request.IsActive;
        if (willBeActive && !section.IsActive)
        {
            return AppErrors.BusinessRule($"Section '{section.Name}' is inactive. Choose an active section.");
        }

        if (table.IsActive && !willBeActive && !table.CanBeDeactivated)
        {
            return AppErrors.InvalidState($"Table {table.Code} is in use ({table.Status}) and cannot be deactivated.", TableMapping.ToDto(table));
        }

        var oldValues = new { table.Code, table.Name, Section = table.Section!.Name, table.Capacity, table.IsActive };
        table.Update(request.Code, request.Name, section.Id, request.Capacity);
        if (table.IsActive != willBeActive)
        {
            _tables.SetActive(table, willBeActive);
        }

        _audit.Add(AuditActions.TableUpdated, nameof(Table), table.Id.ToString(),
            oldValues, new { table.Code, table.Name, Section = section.Name, table.Capacity, table.IsActive });

        return await _tables.SaveAsync(table, section.Name, publish: false, cancellationToken);
    }
}
