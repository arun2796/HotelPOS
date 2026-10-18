using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Reservations;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Reservations.Queries;

public sealed record ListReservationsQuery(ReservationQuery Input)
    : IQuery<PagedResult<ReservationDto>>, IHasInput<ReservationQuery>, IRequirePermission
{
    public string Permission => Permissions.ReservationsView;
}

public sealed class ReservationQueryValidator : AbstractValidator<ReservationQuery>
{
    public ReservationQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PagedQuery.MaxPageSize);
    }
}

internal sealed class ListReservationsHandler : IQueryHandler<ListReservationsQuery, PagedResult<ReservationDto>>
{
    private readonly IAppDbContext _db;

    public ListReservationsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<PagedResult<ReservationDto>>> Handle(ListReservationsQuery request, CancellationToken cancellationToken)
    {
        var query = request.Input;
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize);

        var reservations = _db.Reservations.AsNoTracking()
            .Include(r => r.Table)
            .ThenInclude(t => t!.Section)
            .AsQueryable();

        if (query.TableId is { } tableId)
        {
            reservations = reservations.Where(r => r.TableId == tableId);
        }

        if (query.Status is { } status)
        {
            reservations = reservations.Where(r => r.Status == status);
        }

        if (query.FromUtc is { } fromUtc)
        {
            reservations = reservations.Where(r => r.EndTimeUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            reservations = reservations.Where(r => r.StartTimeUtc <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            reservations = reservations.Where(r => r.CustomerName.ToLower().Contains(search) || r.CustomerPhone.Contains(search));
        }

        var total = await reservations.CountAsync(cancellationToken);
        var items = await reservations
            .OrderBy(r => r.StartTimeUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ReservationDto>
        {
            Items = items.Select(ReservationMapping.ToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
    }
}

