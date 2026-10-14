using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.DayEnd.Commands;

// Stores where the report pack of a closed day was written (the last step of RunDayEnd).
internal sealed record RecordDayEndPackCommand(DateOnly Day, string Folder, IReadOnlyList<string> Files) : ICommand, IAuditedCommand;

internal sealed class RecordDayEndPackHandler : ICommandHandler<RecordDayEndPackCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public RecordDayEndPackHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result> Handle(RecordDayEndPackCommand command, CancellationToken cancellationToken)
    {
        var close = await _db.BusinessDayCloses.FirstOrDefaultAsync(c => c.BusinessDay == command.Day, cancellationToken);
        if (close is null)
        {
            return AppErrors.NotFound("Business day", command.Day);
        }

        close.RecordPack(command.Folder);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.DayEndPackWritten, "BusinessDay", command.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            newValues: new { command.Folder, command.Files });
        return Result.Success();
    }
}
