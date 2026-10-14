using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Operations.Queries;

// The settings every terminal needs (restaurant name, currency, ...); open to every signed-in role.
public sealed record GetPublicSettingsQuery : IQuery<IReadOnlyList<SettingDto>>;

internal sealed class GetPublicSettingsHandler : IQueryHandler<GetPublicSettingsQuery, IReadOnlyList<SettingDto>>
{
    private readonly IAppDbContext _db;

    public GetPublicSettingsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<SettingDto>>> Handle(GetPublicSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await _db.Settings.AsNoTracking().Where(s => s.IsPublic).OrderBy(s => s.Key).ToListAsync(cancellationToken);
        return settings.Select(SettingMapping.ToDto).ToList();
    }
}
