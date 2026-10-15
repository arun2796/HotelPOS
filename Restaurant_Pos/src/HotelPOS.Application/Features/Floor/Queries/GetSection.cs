using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Floor.Queries;

public sealed record GetSectionQuery(int Id) : IQuery<SectionDto>, IRequirePermission
{
    public string Permission => Permissions.TablesView;
}

internal sealed class GetSectionHandler : IQueryHandler<GetSectionQuery, SectionDto>
{
    private readonly FloorSections _sections;

    public GetSectionHandler(FloorSections sections)
    {
        _sections = sections;
    }

    public Task<Result<SectionDto>> Handle(GetSectionQuery request, CancellationToken cancellationToken) =>
        _sections.GetAsync(request.Id, cancellationToken);
}
