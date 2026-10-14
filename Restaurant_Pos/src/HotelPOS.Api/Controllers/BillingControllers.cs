using HotelPOS.Api.Common;
using HotelPOS.Application.Features.Billing.Commands;
using HotelPOS.Application.Features.Billing.Queries;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

internal static class BillingRoles
{
    public const string Desk = Roles.Admin + "," + Roles.Manager + "," + Roles.Cashier;
    public const string DeskAndWaiters = Desk + "," + Roles.Waiter;
    public const string Managers = Roles.Admin + "," + Roles.Manager;
}

[Route("api/billing")]
[Authorize]
public sealed class BillingController : ApiControllerBase
{
    [Authorize(Policy = Permissions.BillingOperate)]
    [HttpGet("pending")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BillSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pending(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListPendingBillsQuery(), cancellationToken));

    [Authorize(Policy = Permissions.BillingOperate)]
    [HttpGet("closed")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BillSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Closed([FromQuery] ClosedBillQuery query, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListClosedBillsQuery(query), cancellationToken));

    [HttpGet("{id:int}")]
    [Authorize(Roles = BillingRoles.DeskAndWaiters)]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetBillQuery(id), cancellationToken));

    [Authorize(Policy = Permissions.BillingOperate)]
    [HttpPost("{id:int}/claim")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Claim(int id, [FromBody] ClaimBillRequest? request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ClaimBillCommand(id, request ?? new ClaimBillRequest()), cancellationToken));

    [Authorize(Policy = Permissions.BillingOperate)]
    [HttpPost("{id:int}/release")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Release(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ReleaseBillCommand(id), cancellationToken));

    [Authorize(Policy = Permissions.BillingDiscount)]
    [HttpPut("{id:int}/discount")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDiscount(int id, ApplyDiscountRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ApplyBillDiscountCommand(id, request), cancellationToken), message: "Discount applied.");

    [Authorize(Policy = Permissions.BillingDiscount)]
    [HttpDelete("{id:int}/discount")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearDiscount(int id, [FromQuery] string? rowVersion, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ClearBillDiscountCommand(id, rowVersion ?? string.Empty), cancellationToken),
            message: "Discount removed.");

    [Authorize(Policy = Permissions.BillingOperate)]
    [HttpPut("{id:int}/customer")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetCustomer(int id, UpdateCustomerRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetBillCustomerCommand(id, request), cancellationToken), message: "Customer details saved.");

    [Authorize(Policy = Permissions.BillingOperate)]
    [HttpPost("{id:int}/finalize")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Finalize(int id, BillActionRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new FinalizeBillCommand(id, request), cancellationToken), message: "Invoice finalized.");

    [Authorize(Policy = Permissions.BillingOperate)]
    [HttpPost("{id:int}/payments")]
    [Idempotent]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddPayment(int id, AddPaymentRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new AddPaymentCommand(id, request, IdempotencyKey), cancellationToken), message: "Payment received.");

    [Authorize(Policy = Permissions.BillingOperate)]
    [HttpPost("{id:int}/close")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Close(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CloseBillCommand(id), cancellationToken), message: "Order closed.");

    [Authorize(Policy = Permissions.BillingOperate)]
    [HttpPost("{id:int}/reopen")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reopen(int id, ReopenBillRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ReopenBillCommand(id, request), cancellationToken), message: "Bill reopened; the order can be changed again.");

    // Cashiers may call void and refund too: the command then requires a manager's approval in the request.
    [Authorize(Policy = Permissions.BillingOperate)]
    [HttpPost("{id:int}/void")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Void(int id, VoidBillRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new VoidBillCommand(id, request), cancellationToken), message: "Bill voided.");

    [Authorize(Policy = Permissions.BillingOperate)]
    [HttpPost("{id:int}/refunds")]
    [Idempotent]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refund(int id, RefundRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new RefundPaymentCommand(id, request, IdempotencyKey), cancellationToken), message: "Refund recorded.");

    private Guid IdempotencyKey => Guid.Parse(Request.Headers[PosHeaders.IdempotencyKey].ToString());
}

[Route("api/discounts")]
[Authorize(Policy = Permissions.BillingOperate)]
public sealed class DiscountsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<DiscountDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListDiscountsQuery(includeInactive), cancellationToken));

    [HttpPost]
    [Authorize(Roles = BillingRoles.Managers)]
    [ProducesResponseType<ApiResponse<DiscountDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveDiscountRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateDiscountCommand(request), cancellationToken), StatusCodes.Status201Created, "Discount created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = BillingRoles.Managers)]
    [ProducesResponseType<ApiResponse<DiscountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, SaveDiscountRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateDiscountCommand(id, request), cancellationToken), message: "Discount updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = BillingRoles.Managers)]
    [ProducesResponseType<ApiResponse<DiscountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeactivateDiscountCommand(id), cancellationToken), message: "Discount deactivated.");
}

[Route("api/payment-methods")]
[Authorize]
public sealed class PaymentMethodsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PaymentMethodDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListPaymentMethodsQuery(includeInactive), cancellationToken));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<PaymentMethodDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SavePaymentMethodRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreatePaymentMethodCommand(request), cancellationToken), StatusCodes.Status201Created, "Payment method created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<PaymentMethodDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, SavePaymentMethodRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdatePaymentMethodCommand(id, request), cancellationToken), message: "Payment method updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<PaymentMethodDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeactivatePaymentMethodCommand(id), cancellationToken), message: "Payment method deactivated.");
}
