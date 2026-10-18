using HotelPOS.Application.Features.Finance.Commands;
using HotelPOS.Application.Features.Finance.Queries;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Finance;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/expenses")]
[Authorize]
public sealed class ExpensesController : ApiControllerBase
{
    [HttpGet("categories")]
    [Authorize(Policy = Permissions.ExpensesView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ExpenseCategoryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCategories([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListExpenseCategoriesQuery(includeInactive), cancellationToken));

    [HttpPost("categories")]
    [Authorize(Policy = Permissions.ExpensesManage)]
    [ProducesResponseType<ApiResponse<ExpenseCategoryDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCategory(CreateExpenseCategoryRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateExpenseCategoryCommand(request), cancellationToken), StatusCodes.Status201Created, "Expense category created.");

    [HttpPut("categories/{id:int}")]
    [Authorize(Policy = Permissions.ExpensesManage)]
    [ProducesResponseType<ApiResponse<ExpenseCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCategory(int id, UpdateExpenseCategoryRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateExpenseCategoryCommand(id, request), cancellationToken), message: "Expense category updated.");

    [HttpPost("categories/{id:int}/activate")]
    [Authorize(Policy = Permissions.ExpensesManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateCategory(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetExpenseCategoryActiveCommand(id, true), cancellationToken), message: "Expense category activated.");

    [HttpPost("categories/{id:int}/deactivate")]
    [Authorize(Policy = Permissions.ExpensesManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeactivateCategory(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetExpenseCategoryActiveCommand(id, false), cancellationToken), message: "Expense category deactivated.");

    [HttpGet]
    [Authorize(Policy = Permissions.ExpensesView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ExpenseDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ExpenseFilter filter, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListExpensesQuery(filter), cancellationToken));

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.ExpensesView)]
    [ProducesResponseType<ApiResponse<ExpenseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetExpenseByIdQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.ExpensesManage)]
    [ProducesResponseType<ApiResponse<ExpenseDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateExpenseRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateExpenseCommand(request), cancellationToken), StatusCodes.Status201Created, "Expense recorded.");

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.ExpensesManage)]
    [ProducesResponseType<ApiResponse<ExpenseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateExpenseRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateExpenseCommand(id, request), cancellationToken), message: "Expense updated.");

    [HttpPost("{id:int}/void")]
    [Authorize(Policy = Permissions.ExpensesManage)]
    [ProducesResponseType<ApiResponse<ExpenseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Void(int id, VoidExpenseRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new VoidExpenseCommand(id, request), cancellationToken), message: "Expense voided.");
}

