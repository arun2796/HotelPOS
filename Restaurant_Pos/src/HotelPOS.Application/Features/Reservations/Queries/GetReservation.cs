using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reservations;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Reservations.Queries;

public sealed record GetReservationQuery(int Id) : IQuery<ReservationDto>, IRequirePermission
{
    public string Permission => Permissions.ReservationsView;
}

internal sealed class GetReservationHandler : IQueryHandler<GetReservationQuery, ReservationDto>
{
    private readonly IAppDbContext _db;

    public GetReservationHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<ReservationDto>> Handle(GetReservationQuery request, CancellationToken cancellationToken)
    {
        var reservation = await _db.Reservations.AsNoTracking()
            .Include(r => r.Table)
            .ThenInclude(t => t!.Section)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (reservation is null)
        {
            return AppErrors.NotFound("Reservation", request.Id);
        }

        return ReservationMapping.ToDto(reservation);
    }
}

