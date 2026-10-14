using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using MediatR;

namespace HotelPOS.Application.Features.Operations.Commands;

// Deliberately not an ICommand, so TransactionBehavior leaves it alone: the backup commits its Running row before
// pg_dump starts (the row is part of the dump, and a server stop leaves it to be closed as interrupted), records a
// failure as well as a success, and must not hold a transaction open for the minutes the dump can take.
// IBackupService (Infrastructure) owns the steps and their audit entries, as the daily schedule uses it too.
public sealed record RunBackupCommand : IRequest<Result<BackupDto>>, IRequirePermission
{
    public string Permission => Permissions.BackupManage;
}

internal sealed class RunBackupHandler : IRequestHandler<RunBackupCommand, Result<BackupDto>>
{
    private readonly IBackupService _backups;

    public RunBackupHandler(IBackupService backups)
    {
        _backups = backups;
    }

    public Task<Result<BackupDto>> Handle(RunBackupCommand request, CancellationToken cancellationToken) =>
        _backups.RunAsync(BackupTrigger.Manual, cancellationToken);
}
