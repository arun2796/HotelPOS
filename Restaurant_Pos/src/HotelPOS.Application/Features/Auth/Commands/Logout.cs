using FluentValidation;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Auth;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Auth.Commands;

// Anonymous: the refresh token itself is the proof.
public sealed record LogoutCommand(LogoutRequest Input) : ICommand, IHasInput<LogoutRequest>;

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
    }
}

internal sealed class LogoutHandler : ICommandHandler<LogoutCommand>
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;

    public LogoutHandler(IAppDbContext db, ITokenService tokens, IClock clock)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        var hash = _tokens.HashRefreshToken(command.Input.RefreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is not null && !stored.IsRevoked)
        {
            stored.Revoke(_clock.UtcNow, "Logout");
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Logging out is idempotent: an unknown or already revoked token is not an error.
        return Result.Success();
    }
}
