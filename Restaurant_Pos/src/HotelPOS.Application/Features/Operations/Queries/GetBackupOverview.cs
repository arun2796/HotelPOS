using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Operations.Queries;

public sealed record GetBackupOverviewQuery : IQuery<BackupOverviewDto>, IRequirePermission
{
    public string Permission => Permissions.BackupManage;
}

internal sealed class GetBackupOverviewHandler : IQueryHandler<GetBackupOverviewQuery, BackupOverviewDto>
{
    private readonly IBackupService _backups;

    public GetBackupOverviewHandler(IBackupService backups)
    {
        _backups = backups;
    }

    public async Task<Result<BackupOverviewDto>> Handle(GetBackupOverviewQuery request, CancellationToken cancellationToken) =>
        await _backups.GetOverviewAsync(cancellationToken);
}
