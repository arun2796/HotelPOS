using HotelPOS.Contracts.Floor;

namespace HotelPOS.Client.Services;

/// <summary>Phase 22 P3: the section floor plan (read only; waiters have tables.view) plus the List/Map choice kept on this device.</summary>
public sealed class FloorLayoutApi
{
    public const string ViewModeKey = "hotelpos.tables.view-mode";
    public const string ListMode = "list";
    public const string MapMode = "map";

    private readonly WebApiClient _api;
    private readonly IBrowserStorage _storage;

    public FloorLayoutApi(WebApiClient api, IBrowserStorage storage)
    {
        _api = api;
        _storage = storage;
    }

    public Task<ApiResult<FloorLayoutDto>> GetLayoutAsync(int sectionId, CancellationToken ct = default) =>
        _api.GetAsync<FloorLayoutDto>($"api/sections/{sectionId}/layout", ct);

    /// <summary>"list", "map", or null when the person has never chosen (the page then follows the screen width).</summary>
    public async Task<string?> GetViewModeAsync()
    {
        try
        {
            var value = await _storage.GetAsync(ViewModeKey);
            return value is ListMode or MapMode ? value : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task SetViewModeAsync(string mode)
    {
        try
        {
            await _storage.SetAsync(ViewModeKey, mode);
        }
        catch
        {
            // A device that cannot store keeps the choice for this visit only.
        }
    }
}
