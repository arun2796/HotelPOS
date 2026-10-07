using HotelPOS.Api.Common;
using HotelPOS.Application.Billing;
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
[Authorize(Roles = BillingRoles.Desk)]
public sealed class BillingController : ApiControllerBase
{
    private readonly IBillingService _billing;

    public BillingController(IBillingService billing)
    {
        _billing = billing;
    }

    [HttpGet("pending")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BillSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pending(CancellationToken cancellationToken) =>
        Envelope(await _billing.ListPendingAsync(cancellationToken));

    [HttpGet("closed")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BillSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Closed([FromQuery] ClosedBillQuery query, CancellationToken cancellationToken) =>
        Envelope(await _billing.ListClosedAsync(query, cancellationToken));

    [HttpGet("{id:int}")]
    [Authorize(Roles = BillingRoles.DeskAndWaiters)]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await _billing.GetAsync(id, cancellationToken));

    [HttpPost("{id:int}/claim")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Claim(int id, [FromBody] ClaimBillRequest? request, CancellationToken cancellationToken) =>
        FromResult(await _billing.ClaimAsync(id, request ?? new ClaimBillRequest(), cancellationToken));

    [HttpPost("{id:int}/release")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Release(int id, CancellationToken cancellationToken) =>
        FromResult(await _billing.ReleaseAsync(id, cancellationToken));

    [HttpPut("{id:int}/discount")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDiscount(int id, ApplyDiscountRequest request, CancellationToken cancellationToken) =>
        FromResult(await _billing.SetDiscountAsync(id, request, cancellationToken), message: "Discount applied.");

    [HttpDelete("{id:int}/discount")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearDiscount(int id, [FromQuery] string? rowVersion, CancellationToken cancellationToken) =>
        FromResult(await _billing.ClearDiscountAsync(id, new BillActionRequest { RowVersion = rowVersion ?? string.Empty }, cancellationToken),
            message: "Discount removed.");

    [HttpPut("{id:int}/customer")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetCustomer(int id, UpdateCustomerRequest request, CancellationToken cancellationToken) =>
        FromResult(await _billing.SetCustomerAsync(id, request, cancellationToken), message: "Customer details saved.");

    [HttpPost("{id:int}/finalize")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Finalize(int id, BillActionRequest request, CancellationToken cancellationToken) =>
        FromResult(await _billing.FinalizeAsync(id, request, cancellationToken), message: "Invoice finalized.");

    [HttpPost("{id:int}/payments")]
    [Idempotent]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddPayment(int id, AddPaymentRequest request, CancellationToken cancellationToken) =>
        FromResult(await _billing.AddPaymentAsync(id, request, IdempotencyKey, cancellationToken), message: "Payment received.");

    [HttpPost("{id:int}/close")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Close(int id, CancellationToken cancellationToken) =>
        FromResult(await _billing.CloseAsync(id, cancellationToken), message: "Order closed.");

    [HttpPost("{id:int}/reopen")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reopen(int id, ReopenBillRequest request, CancellationToken cancellationToken) =>
        FromResult(await _billing.ReopenAsync(id, request, cancellationToken), message: "Bill reopened; the order can be changed again.");

    // Cashiers may call void and refund too: the service then requires a manager's approval in the request.
    [HttpPost("{id:int}/void")]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Void(int id, VoidBillRequest request, CancellationToken cancellationToken) =>
        FromResult(await _billing.VoidAsync(id, request, cancellationToken), message: "Bill voided.");

    [HttpPost("{id:int}/refunds")]
    [Idempotent]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refund(int id, RefundRequest request, CancellationToken cancellationToken) =>
        FromResult(await _billing.RefundAsync(id, request, IdempotencyKey, cancellationToken), message: "Refund recorded.");

    private Guid IdempotencyKey => Guid.Parse(Request.Headers[PosHeaders.IdempotencyKey].ToString());
}

[Route("api/discounts")]
[Authorize(Roles = BillingRoles.Desk)]
public sealed class DiscountsController : ApiControllerBase
{
    private readonly IDiscountService _discounts;

    public DiscountsController(IDiscountService discounts)
    {
        _discounts = discounts;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<DiscountDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        Envelope(await _discounts.ListAsync(includeInactive, cancellationToken));

    [HttpPost]
    [Authorize(Roles = BillingRoles.Managers)]
    [ProducesResponseType<ApiResponse<DiscountDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveDiscountRequest request, CancellationToken cancellationToken) =>
        FromResult(await _discounts.CreateAsync(request, cancellationToken), StatusCodes.Status201Created, "Discount created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = BillingRoles.Managers)]
    [ProducesResponseType<ApiResponse<DiscountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, SaveDiscountRequest request, CancellationToken cancellationToken) =>
        FromResult(await _discounts.UpdateAsync(id, request, cancellationToken), message: "Discount updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = BillingRoles.Managers)]
    [ProducesResponseType<ApiResponse<DiscountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await _discounts.DeactivateAsync(id, cancellationToken), message: "Discount deactivated.");
}

[Route("api/payment-methods")]
[Authorize]
public sealed class PaymentMethodsController : ApiControllerBase
{
    private readonly IPaymentMethodService _methods;

    public PaymentMethodsController(IPaymentMethodService methods)
    {
        _methods = methods;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PaymentMethodDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        Envelope(await _methods.ListAsync(includeInactive, cancellationToken));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<PaymentMethodDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SavePaymentMethodRequest request, CancellationToken cancellationToken) =>
        FromResult(await _methods.CreateAsync(request, cancellationToken), StatusCodes.Status201Created, "Payment method created.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<PaymentMethodDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, SavePaymentMethodRequest request, CancellationToken cancellationToken) =>
        FromResult(await _methods.UpdateAsync(id, request, cancellationToken), message: "Payment method updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<PaymentMethodDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await _methods.DeactivateAsync(id, cancellationToken), message: "Payment method deactivated.");
}
