using System.Globalization;
using System.Text;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Customers;

namespace HotelPOS.Desktop.Services.Api;

public interface ICustomersApi
{
    Task<ApiResult<PagedResult<CustomerDto>>> SearchAsync(CustomerQuery query, CancellationToken cancellationToken = default);

    Task<ApiResult<CustomerDto>> CreateAsync(SaveCustomerRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<CustomerDto>> UpdateAsync(int id, SaveCustomerRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<CustomerDto>> SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default);
}

public sealed class CustomersApi : ICustomersApi
{
    private readonly IApiClient _api;

    public CustomersApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<PagedResult<CustomerDto>>> SearchAsync(CustomerQuery query, CancellationToken cancellationToken = default)
    {
        var url = new StringBuilder("api/customers?page=").Append(query.Page.ToString(CultureInfo.InvariantCulture))
            .Append("&pageSize=").Append(query.PageSize.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            url.Append("&search=").Append(Uri.EscapeDataString(query.Search.Trim()));
        }

        if (query.IncludeInactive)
        {
            url.Append("&includeInactive=true");
        }

        return _api.GetAsync<PagedResult<CustomerDto>>(url.ToString(), cancellationToken);
    }

    public Task<ApiResult<CustomerDto>> CreateAsync(SaveCustomerRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<CustomerDto>("api/customers", request, cancellationToken);

    public Task<ApiResult<CustomerDto>> UpdateAsync(int id, SaveCustomerRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<CustomerDto>($"api/customers/{id}", request, cancellationToken);

    public Task<ApiResult<CustomerDto>> SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default) =>
        _api.PostAsync<CustomerDto>($"api/customers/{id}/{(active ? "activate" : "deactivate")}", null, cancellationToken);
}
