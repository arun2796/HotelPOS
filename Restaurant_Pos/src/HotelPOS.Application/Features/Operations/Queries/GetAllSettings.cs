using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Operations.Queries;

public sealed record GetAllSettingsQuery : IQuery<IReadOnlyList<SettingDto>>, IRequirePermission
{
    public string Permission => Permissions.SettingsManage;
}

internal sealed class GetAllSettingsHandler : IQueryHandler<GetAllSettingsQuery, IReadOnlyList<SettingDto>>
{
    private readonly IAppDbContext _db;

    public GetAllSettingsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<SettingDto>>> Handle(GetAllSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await _db.Settings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(cancellationToken);
        return settings.Select(SettingMapping.ToDto).ToList();
    }
}
