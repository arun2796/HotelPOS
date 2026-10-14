using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;

namespace HotelPOS.Application.Common.Interfaces;

public interface IBackupService
{
    // Database dump (pg_dump custom format, verified with pg_restore --list) plus a zip of the menu pictures.
    Task<Result<BackupDto>> RunAsync(BackupTrigger trigger, CancellationToken cancellationToken = default);

    Task<BackupOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);
}
