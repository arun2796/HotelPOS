using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Print;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Print.Commands;

// A terminal printed a document again; only the audit entry records it. The cashier desk: the endpoint gates by
// role, there is no permission.
public sealed record RecordReprintCommand(ReprintRequest Input) : ICommand, IHasInput<ReprintRequest>, IAuditedCommand;

public sealed class ReprintRequestValidator : AbstractValidator<ReprintRequest>
{
    public ReprintRequestValidator()
    {
        RuleFor(x => x.DocumentType).IsInEnum();
        RuleFor(x => x.EntityId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for the reprint.").MaximumLength(PrintLimits.ReasonMaxLength);
    }
}

internal sealed class RecordReprintHandler : ICommandHandler<RecordReprintCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly ICurrentUser _currentUser;

    public RecordReprintHandler(IAppDbContext db, IAuditTrail audit, ICurrentUser currentUser)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(RecordReprintCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var (entityType, exists, label) = request.DocumentType switch
        {
            PrintDocumentType.Invoice => ("Bill", await _db.Bills.AnyAsync(b => b.Id == request.EntityId, cancellationToken), "Invoice"),
            PrintDocumentType.Receipt => ("Payment", await _db.Payments.AnyAsync(p => p.Id == request.EntityId, cancellationToken), "Receipt"),
            _ => ("KitchenTicket", await _db.KitchenOrders.AnyAsync(k => k.Id == request.EntityId, cancellationToken), "Kitchen ticket"),
        };
        if (!exists)
        {
            return AppErrors.NotFound(label, request.EntityId);
        }

        _audit.Add(AuditActions.PrintReprint, entityType, request.EntityId.ToString(CultureInfo.InvariantCulture),
            newValues: new { request.DocumentType, request.Reason, Device = _currentUser.DeviceName });
        return Result.Success();
    }
}
