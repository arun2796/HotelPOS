using System.Globalization;
using System.Text;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Reservations;

namespace HotelPOS.Desktop.Services.Api;

public interface IReservationsApi
{
    Task<ApiResult<PagedResult<ReservationDto>>> ListAsync(ReservationQuery query, CancellationToken cancellationToken = default);
    Task<ApiResult<ReservationDto>> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<ApiResult<ReservationDto>> CreateAsync(CreateReservationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResult<ReservationDto>> UpdateAsync(int id, UpdateReservationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResult<ReservationDto>> CancelAsync(int id, CancelReservationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResult<ReservationDto>> SeatAsync(int id, SeatReservationRequest request, CancellationToken cancellationToken = default);
    Task<ApiResult<ReservationDto>> MarkNoShowAsync(int id, MarkNoShowRequest request, CancellationToken cancellationToken = default);
}

public sealed class ReservationsApi : IReservationsApi
{
    private readonly IApiClient _api;

    public ReservationsApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<PagedResult<ReservationDto>>> ListAsync(ReservationQuery query, CancellationToken cancellationToken = default)
    {
        var url = new StringBuilder("api/reservations?page=").Append(query.Page.ToString(CultureInfo.InvariantCulture))
            .Append("&pageSize=").Append(query.PageSize.ToString(CultureInfo.InvariantCulture));

        if (query.TableId is { } tableId)
        {
            url.Append("&tableId=").Append(tableId.ToString(CultureInfo.InvariantCulture));
        }

        if (query.Status is { } status)
        {
            url.Append("&status=").Append(((int)status).ToString(CultureInfo.InvariantCulture));
        }

        if (query.FromUtc is { } fromUtc)
        {
            url.Append("&fromUtc=").Append(Uri.EscapeDataString(fromUtc.ToString("O")));
        }

        if (query.ToUtc is { } toUtc)
        {
            url.Append("&toUtc=").Append(Uri.EscapeDataString(toUtc.ToString("O")));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            url.Append("&search=").Append(Uri.EscapeDataString(query.Search.Trim()));
        }

        return _api.GetAsync<PagedResult<ReservationDto>>(url.ToString(), cancellationToken);
    }

    public Task<ApiResult<ReservationDto>> GetAsync(int id, CancellationToken cancellationToken = default) =>
        _api.GetAsync<ReservationDto>($"api/reservations/{id}", cancellationToken);

    public Task<ApiResult<ReservationDto>> CreateAsync(CreateReservationRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<ReservationDto>("api/reservations", request, cancellationToken);

    public Task<ApiResult<ReservationDto>> UpdateAsync(int id, UpdateReservationRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<ReservationDto>($"api/reservations/{id}", request, cancellationToken);

    public Task<ApiResult<ReservationDto>> CancelAsync(int id, CancelReservationRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<ReservationDto>($"api/reservations/{id}/cancel", request, cancellationToken);

    public Task<ApiResult<ReservationDto>> SeatAsync(int id, SeatReservationRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<ReservationDto>($"api/reservations/{id}/seat", request, cancellationToken);

    public Task<ApiResult<ReservationDto>> MarkNoShowAsync(int id, MarkNoShowRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<ReservationDto>($"api/reservations/{id}/no-show", request, cancellationToken);
}

