using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/admin")]
[Authorize(Policy = Permissions.BackupManage)]
public sealed class AdminBackupsController : ApiControllerBase
{
    private readonly IBackupService _backups;

    public AdminBackupsController(IBackupService backups)
    {
        _backups = backups;
    }

    // Runs while the request waits (seconds for a venue-sized database); the client shows a busy indicator.
    [HttpPost("backup")]
    [ProducesResponseType<ApiResponse<BackupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BackupNow(CancellationToken cancellationToken) =>
        FromResult(await _backups.RunAsync(BackupTrigger.Manual, cancellationToken), message: "Backup completed.");

    [HttpGet("backups")]
    [ProducesResponseType<ApiResponse<BackupOverviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Envelope(await _backups.GetOverviewAsync(cancellationToken));
}
