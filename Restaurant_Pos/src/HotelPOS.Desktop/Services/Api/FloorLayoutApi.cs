using HotelPOS.Contracts.Floor;

namespace HotelPOS.Desktop.Services.Api;

/// <summary>Floor-plan geometry of one section (Phase 22 P2): read by every table operator, saved by Admin only.</summary>
public interface IFloorLayoutApi
{
    Task<ApiResult<FloorLayoutDto>> GetAsync(int sectionId, CancellationToken cancellationToken = default);

    Task<ApiResult<FloorLayoutDto>> SaveAsync(int sectionId, SaveFloorLayoutRequest request, CancellationToken cancellationToken = default);
}

public sealed class FloorLayoutApi : IFloorLayoutApi
{
    private readonly IApiClient _api;

    public FloorLayoutApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<FloorLayoutDto>> GetAsync(int sectionId, CancellationToken cancellationToken = default) =>
        _api.GetAsync<FloorLayoutDto>($"api/sections/{sectionId}/layout", cancellationToken);

    public Task<ApiResult<FloorLayoutDto>> SaveAsync(int sectionId, SaveFloorLayoutRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<FloorLayoutDto>($"api/sections/{sectionId}/layout", request, cancellationToken);
}
