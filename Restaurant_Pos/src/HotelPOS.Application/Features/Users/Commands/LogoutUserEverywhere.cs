using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Features.Users.Commands;

// Revokes every refresh token of the user, so each terminal has to log in again when its access token expires.
public sealed record LogoutUserEverywhereCommand(int Id) : ICommand, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.UsersManage;
}

internal sealed class LogoutUserEverywhereHandler : ICommandHandler<LogoutUserEverywhereCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IClock _clock;
    private readonly ILogger<LogoutUserEverywhereHandler> _logger;

    public LogoutUserEverywhereHandler(IAppDbContext db, IAuditTrail audit, IClock clock, ILogger<LogoutUserEverywhereHandler> logger)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> Handle(LogoutUserEverywhereCommand command, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == command.Id, cancellationToken);
        if (user is null)
        {
            return AppErrors.NotFound("User", command.Id);
        }

        var now = _clock.UtcNow;
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.UserId == command.Id && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(now, "Logged out everywhere");
        }

        _audit.Add(AuditActions.UserLoggedOutAll, nameof(User), user.Id.ToString(),
            newValues: new { RevokedSessionsCount = activeTokens.Count });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Revoked all ({Count}) active sessions for user {Username}", activeTokens.Count, user.Username);
        return Result.Success();
    }
}
