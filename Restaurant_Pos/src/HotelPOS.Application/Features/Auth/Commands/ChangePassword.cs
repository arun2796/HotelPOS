using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Users;
using HotelPOS.Contracts.Auth;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Auth.Commands;

// The signed-in user's own password; every signed-in role may change it.
public sealed record ChangePasswordCommand(ChangePasswordRequest Input) : ICommand, IHasInput<ChangePasswordRequest>, IAuditedCommand;

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).ValidPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must be different from the current one.");
    }
}

internal sealed class ChangePasswordHandler : ICommandHandler<ChangePasswordCommand>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditTrail _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public ChangePasswordHandler(IAppDbContext db, IPasswordHasher hasher, IAuditTrail audit, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        if (_currentUser.UserId is not { } userId)
        {
            return AppErrors.Unauthenticated();
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return AppErrors.Unauthenticated();
        }

        if (!PasswordPolicy.MeetsComplexity(request.NewPassword))
        {
            return AppErrors.Validation(nameof(request.NewPassword).ToCamelCase(), "Password must be at least 8 characters long and contain both letters and digits.");
        }

        if (_hasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordCheck.Failed)
        {
            return AppErrors.Validation(nameof(request.CurrentPassword).ToCamelCase(), "The current password is not correct.");
        }

        user.SetPasswordHash(_hasher.Hash(request.NewPassword), mustChangePassword: false, changedAt: _clock.UtcNow);
        _audit.Add(AuditActions.PasswordChanged, nameof(User), user.Id.ToString());
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
