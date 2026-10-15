using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Queries;

public sealed record ListStationsQuery(bool IncludeInactive) : IQuery<IReadOnlyList<StationDto>>, IRequirePermission
{
    public string Permission => Permissions.MenuView;
}

internal sealed class ListStationsHandler : IQueryHandler<ListStationsQuery, IReadOnlyList<StationDto>>
{
    private readonly IAppDbContext _db;

    public ListStationsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<StationDto>>> Handle(ListStationsQuery request, CancellationToken cancellationToken)
    {
        var stations = await _db.PreparationStations.AsNoTracking()
            .Where(s => request.IncludeInactive || s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
        return stations.Select(MenuMapping.ToDto).ToList();
    }
}
