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

public sealed record CreateTableCommand(CreateTableRequest Input)
    : ICommand<TableDto>, IHasInput<CreateTableRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.TablesManage;
}

public sealed class CreateTableRequestValidator : AbstractValidator<CreateTableRequest>
{
    public CreateTableRequestValidator()
    {
        RuleFor(x => x.Code).ValidTableCode();
        RuleFor(x => x.Name).MaximumLength(FloorLimits.NameMaxLength);
        RuleFor(x => x.SectionId).GreaterThan(0).WithMessage("Select a section.");
        RuleFor(x => x.Capacity).InclusiveBetween(FloorLimits.MinCapacity, FloorLimits.MaxCapacity);
    }
}

internal sealed class CreateTableHandler : ICommandHandler<CreateTableCommand, TableDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public CreateTableHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<TableDto>> Handle(CreateTableCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var section = await _db.Sections.FirstOrDefaultAsync(s => s.Id == request.SectionId, cancellationToken);
        if (section is null)
        {
            return AppErrors.Validation("sectionId", "The selected section does not exist.");
        }

        if (!section.IsActive)
        {
            return AppErrors.BusinessRule($"Section '{section.Name}' is inactive. Activate it before adding tables.");
        }

        var code = Table.NormalizeCode(request.Code);
        if (await _db.Tables.AnyAsync(t => t.Code == code, cancellationToken))
        {
            return AppErrors.Duplicate($"Table code '{code}' is already used.");
        }

        var table = new Table(request.Code, request.Name, section.Id, request.Capacity);
        _db.Tables.Add(table);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.TableCreated, nameof(Table), table.Id.ToString(),
            newValues: new { table.Code, table.Name, Section = section.Name, table.Capacity });

        return TableMapping.ToDto(table, section.Name);
    }
}
