using System.Globalization;
using System.Text;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Finance;

namespace HotelPOS.Desktop.Services.Api;

public interface IExpensesApi
{
    Task<ApiResult<IReadOnlyList<ExpenseDto>>> ListAsync(ExpenseFilter filter, CancellationToken cancellationToken = default);
    Task<ApiResult<ExpenseDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ApiResult<ExpenseDto>> CreateAsync(CreateExpenseRequest request, CancellationToken cancellationToken = default);
    Task<ApiResult<ExpenseDto>> UpdateAsync(int id, UpdateExpenseRequest request, CancellationToken cancellationToken = default);
    Task<ApiResult<ExpenseDto>> VoidAsync(int id, VoidExpenseRequest request, CancellationToken cancellationToken = default);
    Task<ApiResult<IReadOnlyList<ExpenseCategoryDto>>> ListCategoriesAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<ApiResult<ExpenseCategoryDto>> CreateCategoryAsync(CreateExpenseCategoryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResult<ExpenseCategoryDto>> UpdateCategoryAsync(int id, UpdateExpenseCategoryRequest request, CancellationToken cancellationToken = default);
    Task<ApiResult<object>> ActivateCategoryAsync(int id, CancellationToken cancellationToken = default);
    Task<ApiResult<object>> DeactivateCategoryAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class ExpensesApi : IExpensesApi
{
    private readonly IApiClient _api;

    public ExpensesApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<IReadOnlyList<ExpenseDto>>> ListAsync(ExpenseFilter filter, CancellationToken cancellationToken = default)
    {
        var url = new StringBuilder("api/expenses");
        var separator = "?";

        if (filter.From is { } from)
        {
            url.Append(separator).Append("from=").Append(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            separator = "&";
        }

        if (filter.To is { } to)
        {
            url.Append(separator).Append("to=").Append(to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            separator = "&";
        }

        if (filter.CategoryId is { } catId)
        {
            url.Append(separator).Append("categoryId=").Append(catId.ToString(CultureInfo.InvariantCulture));
            separator = "&";
        }

        if (filter.IncludeVoid)
        {
            url.Append(separator).Append("includeVoid=true");
        }

        return _api.GetAsync<IReadOnlyList<ExpenseDto>>(url.ToString(), cancellationToken);
    }

    public Task<ApiResult<ExpenseDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _api.GetAsync<ExpenseDto>($"api/expenses/{id}", cancellationToken);

    public Task<ApiResult<ExpenseDto>> CreateAsync(CreateExpenseRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<ExpenseDto>("api/expenses", request, cancellationToken);

    public Task<ApiResult<ExpenseDto>> UpdateAsync(int id, UpdateExpenseRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<ExpenseDto>($"api/expenses/{id}", request, cancellationToken);

    public Task<ApiResult<ExpenseDto>> VoidAsync(int id, VoidExpenseRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<ExpenseDto>($"api/expenses/{id}/void", request, cancellationToken);

    public Task<ApiResult<IReadOnlyList<ExpenseCategoryDto>>> ListCategoriesAsync(bool includeInactive = false, CancellationToken cancellationToken = default) =>
        _api.GetAsync<IReadOnlyList<ExpenseCategoryDto>>($"api/expenses/categories?includeInactive={(includeInactive ? "true" : "false")}", cancellationToken);

    public Task<ApiResult<ExpenseCategoryDto>> CreateCategoryAsync(CreateExpenseCategoryRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<ExpenseCategoryDto>("api/expenses/categories", request, cancellationToken);

    public Task<ApiResult<ExpenseCategoryDto>> UpdateCategoryAsync(int id, UpdateExpenseCategoryRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<ExpenseCategoryDto>($"api/expenses/categories/{id}", request, cancellationToken);

    public Task<ApiResult<object>> ActivateCategoryAsync(int id, CancellationToken cancellationToken = default) =>
        _api.PostAsync<object>($"api/expenses/categories/{id}/activate", new { }, cancellationToken);

    public Task<ApiResult<object>> DeactivateCategoryAsync(int id, CancellationToken cancellationToken = default) =>
        _api.PostAsync<object>($"api/expenses/categories/{id}/deactivate", new { }, cancellationToken);
}

