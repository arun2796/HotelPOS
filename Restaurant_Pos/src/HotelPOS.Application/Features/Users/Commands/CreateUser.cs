using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Users.Commands;

public sealed record CreateUserCommand(CreateUserRequest Input)
    : ICommand<UserDto>, IHasInput<CreateUserRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.UsersManage;
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .Length(3, 50)
            .Matches("^[A-Za-z0-9._-]+$").WithMessage("Username may contain letters, digits, '.', '-' and '_' only.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).ValidPassword();
        RuleFor(x => x.Roles).ValidRoles();
    }
}

internal sealed class CreateUserHandler : ICommandHandler<CreateUserCommand, UserDto>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditTrail _audit;
    private readonly UserAdministration _users;

    public CreateUserHandler(IAppDbContext db, IPasswordHasher hasher, IAuditTrail audit, UserAdministration users)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
        _users = users;
    }

    public async Task<Result<UserDto>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var normalized = User.Normalize(request.Username);
        if (await _db.Users.AnyAsync(u => u.NormalizedUsername == normalized, cancellationToken))
        {
            return AppErrors.Duplicate($"Username '{request.Username.Trim()}' is already taken.");
        }

        if (!_users.MayManage(request.Roles))
        {
            return UserAdministration.AdminOnly();
        }

        var rolesResult = await _users.LoadRolesAsync(request.Roles, cancellationToken);
        if (rolesResult.IsFailure)
        {
            return rolesResult.Error!;
        }

        var user = new User(request.Username, request.DisplayName, _hasher.Hash(request.Password), request.MustChangePassword);
        user.SetRoles(rolesResult.Value);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(AuditActions.UserCreated, nameof(User), user.Id.ToString(),
            newValues: new { user.Username, user.DisplayName, Roles = user.RoleNames.ToList(), user.MustChangePassword });
        return UserMapping.ToDto(user);
    }
}
