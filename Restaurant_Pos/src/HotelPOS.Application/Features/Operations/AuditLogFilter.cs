using HotelPOS.Contracts.Admin;
using HotelPOS.Domain.Administration;

namespace HotelPOS.Application.Features.Operations;

// The filter shared by the audit log screen and its CSV export.
internal static class AuditLogFilter
{
    public static IQueryable<AuditLog> Apply(IQueryable<AuditLog> queryable, AuditLogQuery filter)
    {
        if (filter.UserId.HasValue)
        {
            queryable = queryable.Where(a => a.UserId == filter.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.UserName))
        {
            var userPattern = filter.UserName.Trim().ToUpperInvariant();
            queryable = queryable.Where(a => a.UserName != null && a.UserName.ToUpper().Contains(userPattern));
        }

        if (filter.DeviceId.HasValue)
        {
            queryable = queryable.Where(a => a.DeviceId == filter.DeviceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.DeviceName))
        {
            var devicePattern = filter.DeviceName.Trim().ToUpperInvariant();
            queryable = queryable.Where(a => a.DeviceName != null && a.DeviceName.ToUpper().Contains(devicePattern));
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            queryable = queryable.Where(a => a.Action == filter.Action.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            queryable = queryable.Where(a => a.EntityType == filter.EntityType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            queryable = queryable.Where(a => a.EntityId == filter.EntityId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = filter.Search.Trim().ToUpperInvariant();
            queryable = queryable.Where(a =>
                (a.UserName != null && a.UserName.ToUpper().Contains(pattern)) ||
                (a.Action != null && a.Action.ToUpper().Contains(pattern)) ||
                (a.DeviceName != null && a.DeviceName.ToUpper().Contains(pattern)));
        }

        if (filter.From.HasValue)
        {
            queryable = queryable.Where(a => a.Timestamp >= filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            queryable = queryable.Where(a => a.Timestamp <= filter.To.Value);
        }

        return queryable;
    }
}
