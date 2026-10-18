using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

// Phase 22 P4: who is on duty right now - hub-connected users, users with an open cash shift, waiters with active orders.
public sealed record GetStaffOnDutyQuery : IQuery<IReadOnlyList<StaffOnDutyDto>>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetStaffOnDutyHandler : IQueryHandler<GetStaffOnDutyQuery, IReadOnlyList<StaffOnDutyDto>>
{
    private readonly IReportQueries _queries;
    private readonly IPresenceTracker _presence;

    public GetStaffOnDutyHandler(IReportQueries queries, IPresenceTracker presence)
    {
        _queries = queries;
        _presence = presence;
    }

    public async Task<Result<IReadOnlyList<StaffOnDutyDto>>> Handle(GetStaffOnDutyQuery request, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<StaffOnDutyDto>>.Success(await StaffOnDutyMerge.LoadAsync(_queries, _presence, cancellationToken));
}

public static class StaffOnDutyMerge
{
    internal static async Task<IReadOnlyList<StaffOnDutyDto>> LoadAsync(IReportQueries queries, IPresenceTracker presence, CancellationToken cancellationToken) =>
        Compose(presence.Snapshot(), await queries.StaffDutyAsync(cancellationToken));

    // Pure merge, one row per user: online first, then by name.
    public static IReadOnlyList<StaffOnDutyDto> Compose(IReadOnlyList<PresenceEntry> presence, IReadOnlyList<StaffDutyRow> duty)
    {
        var rows = new Dictionary<int, StaffOnDutyDto>();

        foreach (var user in presence.GroupBy(p => p.UserId))
        {
            var first = user.First();
            rows[user.Key] = new StaffOnDutyDto
            {
                UserId = user.Key,
                UserName = first.UserName,
                Roles = user.SelectMany(p => p.Roles).Distinct().Order().ToList(),
                IsOnline = true,
                Devices = user.Select(p => p.DeviceName).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Distinct().Order().ToList(),
                ConnectedSinceUtc = user.Min(p => p.ConnectedSinceUtc),
            };
        }

        foreach (var row in duty)
        {
            var roles = row.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var existing = rows.GetValueOrDefault(row.UserId);
            rows[row.UserId] = (existing ?? new StaffOnDutyDto { UserId = row.UserId, UserName = row.UserName, Roles = roles }) with
            {
                HasOpenShift = row.HasOpenShift,
                ShiftDeviceName = row.ShiftDeviceName,
                ActiveOrders = row.ActiveOrders,
            };
        }

        return rows.Values
            .OrderByDescending(r => r.IsOnline)
            .ThenBy(r => r.UserName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
