using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Features.Users.Commands;

// Clears a lockout and the failed-login count before the lockout window ends.
public sealed record UnlockUserCommand(int Id) : ICommand, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.UsersManage;
}

internal sealed class UnlockUserHandler : ICommandHandler<UnlockUserCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IClock _clock;
    private readonly ILogger<UnlockUserHandler> _logger;

    public UnlockUserHandler(IAppDbContext db, IAuditTrail audit, IClock clock, ILogger<UnlockUserHandler> logger)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> Handle(UnlockUserCommand command, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == command.Id, cancellationToken);
        if (user is null)
        {
            return AppErrors.NotFound("User", command.Id);
        }

        var wasLocked = user.IsLockedOut(_clock.UtcNow) || user.FailedLoginCount > 0;
        user.Unlock();

        if (wasLocked)
        {
            _audit.Add(AuditActions.UserUnlocked, nameof(User), user.Id.ToString());
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("User {Username} was unlocked by administrator", user.Username);
        }

        return Result.Success();
    }
}
