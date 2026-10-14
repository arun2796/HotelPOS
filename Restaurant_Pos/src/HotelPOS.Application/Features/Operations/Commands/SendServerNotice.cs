using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Operations.Commands;

// "Server restarting in 2 minutes for maintenance": shown as a banner on every connected terminal.
public sealed record SendServerNoticeCommand(ServerNoticeRequest Input)
    : ICommand, IHasInput<ServerNoticeRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ServerManage;
}

public sealed class ServerNoticeRequestValidator : AbstractValidator<ServerNoticeRequest>
{
    public ServerNoticeRequestValidator()
    {
        RuleFor(x => x.Level).IsInEnum();
        RuleFor(x => x.Message).NotEmpty().MaximumLength(ServerNoticeRequest.MaxMessageLength);
    }
}

internal sealed class SendServerNoticeHandler : ICommandHandler<SendServerNoticeCommand>
{
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly IRealtimeNotifier _realtime;
    private readonly ICurrentUser _currentUser;

    public SendServerNoticeHandler(IAuditTrail audit, IAfterCommit afterCommit, IRealtimeNotifier realtime, ICurrentUser currentUser)
    {
        _audit = audit;
        _afterCommit = afterCommit;
        _realtime = realtime;
        _currentUser = currentUser;
    }

    public Task<Result> Handle(SendServerNoticeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var message = request.Message.Trim();
        _audit.Add(AuditActions.ServerNoticeSent, "Server", null, newValues: new { request.Level, Message = message });
        _afterCommit.Enqueue(() => _realtime.PublishAsync(
            HubEvents.ServerNotice,
            new ServerNoticeEvent { Message = message, Level = request.Level, SentBy = _currentUser.UserName },
            RealtimeAudience.All,
            cancellationToken));
        return Task.FromResult(Result.Success());
    }
}
