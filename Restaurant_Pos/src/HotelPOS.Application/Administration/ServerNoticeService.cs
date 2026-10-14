using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Realtime;

namespace HotelPOS.Application.Administration;

public interface IServerNoticeService
{
    Task SendAsync(ServerNoticeRequest request, CancellationToken cancellationToken = default);
}

// "Server restarting in 2 minutes for maintenance": shown as a banner on every connected terminal.
public sealed class ServerNoticeService : IServerNoticeService
{
    private readonly IAppDbContext _db;
    private readonly IRealtimeNotifier _realtime;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;

    public ServerNoticeService(IAppDbContext db, IRealtimeNotifier realtime, IAuditService audit, ICurrentUser currentUser)
    {
        _db = db;
        _realtime = realtime;
        _audit = audit;
        _currentUser = currentUser;
    }

    public async Task SendAsync(ServerNoticeRequest request, CancellationToken cancellationToken = default)
    {
        var message = request.Message.Trim();
        _audit.Record(AuditActions.ServerNoticeSent, "Server", null, newValues: new { request.Level, Message = message });
        await _db.SaveChangesAsync(cancellationToken);

        await _realtime.PublishAsync(
            HubEvents.ServerNotice,
            new ServerNoticeEvent { Message = message, Level = request.Level, SentBy = _currentUser.UserName },
            RealtimeAudience.All,
            cancellationToken);
    }
}
