using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Kitchen;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Kitchen;

public interface IKitchenService
{
    Task<KitchenTicketListDto> ListAsync(KitchenTicketQuery query, CancellationToken cancellationToken = default);

    Task<Result<KitchenTicketDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<KitchenTicketListDto> ListCompletedAsync(DateOnly? businessDay, CancellationToken cancellationToken = default);

    Task<Result<KitchenTicketDto>> AcceptAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<KitchenTicketDto>> StartAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<KitchenTicketDto>> ReadyAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<KitchenTicketDto>> CompleteAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<KitchenTicketDto>> RecallAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class KitchenService : IKitchenService
{
    private static readonly KitchenOrderStatus[] OpenStatuses =
    {
        KitchenOrderStatus.New, KitchenOrderStatus.Accepted, KitchenOrderStatus.Preparing, KitchenOrderStatus.Ready,
    };

    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly KitchenSync _sync;

    public KitchenService(IAppDbContext db, IAuditService audit, ICurrentUser currentUser, IClock clock, KitchenSync sync)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
        _sync = sync;
    }

    public async Task<KitchenTicketListDto> ListAsync(KitchenTicketQuery query, CancellationToken cancellationToken = default)
    {
        var serverTime = _clock.UtcNow;
        var tickets = Tickets();
        tickets = query.Status is { } status ? tickets.Where(k => k.Status == status) : tickets.Where(k => OpenStatuses.Contains(k.Status));
        if (query.StationId is { } stationId)
        {
            tickets = tickets.Where(k => k.PreparationStationId == stationId);
        }

        return new KitchenTicketListDto
        {
            ServerTimeUtc = serverTime,
            Tickets = await ToDtosAsync(await tickets.OrderBy(k => k.CreatedAt).ToListAsync(cancellationToken), cancellationToken),
        };
    }

    public async Task<Result<KitchenTicketDto>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var ticket = await Tickets().FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
        return ticket is null ? AppErrors.NotFound("Ticket", id) : (await ToDtosAsync(new[] { ticket }, cancellationToken))[0];
    }

    public async Task<KitchenTicketListDto> ListCompletedAsync(DateOnly? businessDay, CancellationToken cancellationToken = default)
    {
        var (from, to) = await BusinessDayAsync(businessDay, cancellationToken);
        var tickets = await Tickets()
            .Where(k => (k.Status == KitchenOrderStatus.Completed && k.CompletedAt >= from && k.CompletedAt < to)
                || (k.Status == KitchenOrderStatus.Cancelled && k.CancelledAt >= from && k.CancelledAt < to))
            .OrderByDescending(k => k.CompletedAt ?? k.CancelledAt)
            .ToListAsync(cancellationToken);
        return new KitchenTicketListDto { ServerTimeUtc = _clock.UtcNow, Tickets = await ToDtosAsync(tickets, cancellationToken) };
    }

    public Task<Result<KitchenTicketDto>> AcceptAsync(int id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, "KitchenTicket.Accepted", (t, user, now) => t.Accept(user, now), cancellationToken);

    public Task<Result<KitchenTicketDto>> StartAsync(int id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, "KitchenTicket.Started", (t, user, now) => t.Start(user, now), cancellationToken);

    public Task<Result<KitchenTicketDto>> ReadyAsync(int id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, "KitchenTicket.Ready", (t, _, now) => t.MarkReady(now), cancellationToken);

    public Task<Result<KitchenTicketDto>> CompleteAsync(int id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, "KitchenTicket.Completed", (t, user, now) => t.Complete(user, now), cancellationToken);

    public Task<Result<KitchenTicketDto>> RecallAsync(int id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, "KitchenTicket.Recalled", (t, _, _) => t.Recall(), cancellationToken);

    private async Task<Result<KitchenTicketDto>> ChangeAsync(
        int id,
        string auditAction,
        Action<KitchenOrder, int, DateTime> change,
        CancellationToken cancellationToken)
    {
        var ticket = await _db.KitchenOrders
            .Include(k => k.Order).ThenInclude(o => o!.Table)
            .FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
        if (ticket is null)
        {
            return AppErrors.NotFound("Ticket", id);
        }

        var wasReady = ticket.Status == KitchenOrderStatus.Ready;
        try
        {
            change(ticket, _currentUser.UserId ?? 0, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return AppErrors.InvalidState(ex.Message, (await GetAsync(id, cancellationToken)).Value);
        }

        var order = ticket.Order!;
        var table = order.Table!;
        var tickets = await _sync.LoadTicketsAsync(order.Id, cancellationToken);
        var kitchenChange = _sync.Recompute(order, tickets, table);
        _audit.Record(auditAction, "KitchenTicket", ticket.Id.ToString(CultureInfo.InvariantCulture),
            newValues: new { ticket.TicketNumber, ticket.Status, Order = order.OrderNumber });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppErrors.Concurrency("ticket", (await GetAsync(id, cancellationToken)).Value);
        }

        var newlyReady = !wasReady && ticket.Status == KitchenOrderStatus.Ready ? new[] { ticket.TicketNumber } : Array.Empty<string>();
        await _sync.PublishAsync(order, table, kitchenChange, Array.Empty<KitchenOrder>(), new[] { ticket }, newlyReady);
        return await GetAsync(id, cancellationToken);
    }

    private IQueryable<KitchenOrder> Tickets() =>
        _db.KitchenOrders.AsNoTracking()
            .Include(k => k.Order).ThenInclude(o => o!.Table)
            .Include(k => k.Items).ThenInclude(i => i.OrderItem).ThenInclude(oi => oi!.Modifiers);

    private async Task<List<KitchenTicketDto>> ToDtosAsync(IReadOnlyCollection<KitchenOrder> tickets, CancellationToken cancellationToken)
    {
        var waiterIds = tickets.Select(t => t.Order!.WaiterId).Distinct().ToList();
        var waiters = await _db.Users.AsNoTracking().Where(u => waiterIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var stations = await _db.PreparationStations.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, cancellationToken);
        return tickets.Select(t => ToDto(t, waiters.GetValueOrDefault(t.Order!.WaiterId, string.Empty), stations.GetValueOrDefault(t.PreparationStationId, string.Empty))).ToList();
    }

    internal static KitchenTicketDto ToDto(KitchenOrder ticket, string waiterName, string stationCode) => new()
    {
        Id = ticket.Id,
        TicketNumber = ticket.TicketNumber,
        OrderId = ticket.OrderId,
        OrderNumber = ticket.Order?.OrderNumber ?? 0,
        BatchNumber = ticket.BatchNumber,
        TableCode = ticket.Order?.Table?.Code ?? string.Empty,
        WaiterName = waiterName,
        StationId = ticket.PreparationStationId,
        StationCode = stationCode,
        Status = ticket.Status,
        CreatedAtUtc = ticket.CreatedAt,
        AcceptedAtUtc = ticket.AcceptedAt,
        StartedAtUtc = ticket.StartedAt,
        ReadyAtUtc = ticket.ReadyAt,
        CompletedAtUtc = ticket.CompletedAt,
        CancelledAtUtc = ticket.CancelledAt,
        Items = ticket.Items.OrderBy(i => i.Id).Select(i => new KitchenTicketItemDto
        {
            OrderItemId = i.OrderItemId,
            Name = i.OrderItem?.ItemName ?? string.Empty,
            Quantity = i.Quantity,
            Notes = i.OrderItem?.Notes,
            Modifiers = i.OrderItem?.Modifiers.Select(m => m.Name).ToList() ?? new List<string>(),
            IsCancelled = i.IsCancelled,
        }).ToList(),
        RowVersion = RowVersions.Encode(ticket.RowVersion),
    };

    private async Task<(DateTime From, DateTime To)> BusinessDayAsync(DateOnly? day, CancellationToken cancellationToken)
    {
        var startText = await _db.Settings.AsNoTracking().Where(s => s.Key == SettingKeys.BusinessDayStartTime).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
        var start = TimeOnly.TryParse(startText, CultureInfo.InvariantCulture, out var parsed) ? parsed : new TimeOnly(4, 0);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, TimeZoneInfo.Local);
        var date = day ?? DateOnly.FromDateTime(TimeOnly.FromDateTime(nowLocal) < start ? nowLocal.AddDays(-1) : nowLocal);
        var fromLocal = date.ToDateTime(start, DateTimeKind.Unspecified);
        var from = TimeZoneInfo.ConvertTimeToUtc(fromLocal, TimeZoneInfo.Local);
        return (from, from.AddDays(1));
    }
}
