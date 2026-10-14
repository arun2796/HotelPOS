using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HotelPOS.Application.Features.Operations.Queries;

// Anonymous: a terminal checks the server and its version before anyone logs in.
public sealed record GetSystemInfoQuery : IQuery<SystemInfoDto>;

internal sealed class GetSystemInfoHandler : IQueryHandler<GetSystemInfoQuery, SystemInfoDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly AppOptions _app;

    public GetSystemInfoHandler(IAppDbContext db, IClock clock, IOptions<AppOptions> app)
    {
        _db = db;
        _clock = clock;
        _app = app.Value;
    }

    public async Task<Result<SystemInfoDto>> Handle(GetSystemInfoQuery request, CancellationToken cancellationToken)
    {
        var name = await _db.Settings.AsNoTracking()
            .Where(s => s.Key == SettingKeys.RestaurantName)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return new SystemInfoDto
        {
            RestaurantName = name ?? "HotelPOS",
            ApiVersion = _app.ApiVersion,
            MinClientVersion = _app.MinClientVersion,
            ServerTimeUtc = _clock.UtcNow,
        };
    }
}
