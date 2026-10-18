using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Floor.Queries;

public sealed record GetFloorLayoutQuery(int SectionId) : IQuery<FloorLayoutDto>, IRequirePermission
{
    public string Permission => Permissions.TablesView;
}

internal sealed class GetFloorLayoutHandler : IQueryHandler<GetFloorLayoutQuery, FloorLayoutDto>
{
    private readonly IAppDbContext _db;

    public GetFloorLayoutHandler(IAppDbContext db)
    {
        _db = db;
    }

    public Task<Result<FloorLayoutDto>> Handle(GetFloorLayoutQuery request, CancellationToken cancellationToken) =>
        FloorLayouts.LoadAsync(_db, request.SectionId, cancellationToken);
}
