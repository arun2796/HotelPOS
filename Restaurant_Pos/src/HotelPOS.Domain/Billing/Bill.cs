using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Domain.Billing;

public sealed class Bill : BaseEntity, IHasRowVersion
{
    private readonly List<BillItem> _items = new();
    private readonly List<Payment> _payments = new();

    private Bill()
    {
    }

    public Bill(Order order, string tableCode, IEnumerable<BillLineInput> lines, bool roundOff)
    {
        OrderId = order.Id;
        TableCode = tableCode;
        WaiterId = order.WaiterId;
        Status = BillStatus.Open;
        PaymentStatus = PaymentStatus.Pending;
        _items.AddRange(lines.Where(l => !l.IsCancelled).Select(l => new BillItem(l)));
        if (_items.Count == 0)
        {
            throw new DomainException($"Order {order.OrderNumber} has nothing to bill. Cancel the order instead.");
        }

        Recalculate(roundOff);
    }

    public int BillNumber { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public int OrderId { get; private set; }
    public string TableCode { get; private set; } = string.Empty;
    public int WaiterId { get; private set; }
    public BillStatus Status { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public decimal Subtotal { get; private set; }
    public int? DiscountId { get; private set; }
    public DiscountType? DiscountType { get; private set; }
    public decimal? DiscountValue { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public string? DiscountReason { get; private set; }
    public int? DiscountApprovedBy { get; private set; }
    public decimal TaxableAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal RoundOff { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal PaidAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }

    // Reserved for a later service-charge feature, so adding it needs no migration.
    public decimal? ServiceChargeAmount { get; private set; }

    public string? CustomerName { get; private set; }
    public string? CustomerPhone { get; private set; }
    public string? CustomerGstin { get; private set; }
    public int? ClaimedByUserId { get; private set; }
    public Guid? ClaimedByDeviceId { get; private set; }
    public DateTime? ClaimedAt { get; private set; }
    public DateTime? ClaimExpiresAt { get; private set; }
    public DateTime? FinalizedAt { get; private set; }
    public DateTime? SettledAt { get; private set; }
    public int? SettledBy { get; private set; }
    public DateTime? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }
    public uint RowVersion { get; set; }

    public IReadOnlyCollection<BillItem> Items => _items;
    public IReadOnlyCollection<Payment> Payments => _payments;

    public decimal BalanceDue => Status is BillStatus.Open or BillStatus.Finalized ? Math.Max(0m, GrandTotal - PaidAmount) : 0m;

    public bool IsPending => Status is BillStatus.Open or BillStatus.Finalized;

    public bool IsHeldByOther(int userId, Guid? deviceId, DateTime nowUtc)
    {
        if (ClaimExpiresAt is null || ClaimExpiresAt <= nowUtc)
        {
            return false;
        }

        // A device is the counter; without one (tests, tools) the user is.
        return ClaimedByDeviceId is not null && deviceId is not null
            ? ClaimedByDeviceId != deviceId
            : ClaimedByUserId != userId;
    }

    public bool IsHeldBy(int userId, Guid? deviceId, DateTime nowUtc) =>
        ClaimExpiresAt > nowUtc && !IsHeldByOther(userId, deviceId, nowUtc);

    public void Claim(int userId, Guid? deviceId, DateTime nowUtc)
    {
        EnsurePending("claimed");
        ClaimedByUserId = userId;
        ClaimedByDeviceId = deviceId;
        ClaimedAt = IsHeldBy(userId, deviceId, nowUtc) ? ClaimedAt : nowUtc;
        ClaimExpiresAt = nowUtc.AddMinutes(BillingLimits.ClaimMinutes);
    }

    public void Release()
    {
        ClaimedByUserId = null;
        ClaimedByDeviceId = null;
        ClaimedAt = null;
        ClaimExpiresAt = null;
    }

    public void ApplyDiscount(int? discountId, DiscountType type, decimal value, string? reason, int? approvedBy, bool roundOff)
    {
        EnsureOpen("discounted");
        Discount.CheckValue(type, value);
        DiscountId = discountId;
        DiscountType = type;
        DiscountValue = value;
        DiscountReason = Clean(reason);
        DiscountApprovedBy = approvedBy;
        Recalculate(roundOff);
    }

    public void ClearDiscount(bool roundOff)
    {
        EnsureOpen("changed");
        DiscountId = null;
        DiscountType = null;
        DiscountValue = null;
        DiscountReason = null;
        DiscountApprovedBy = null;
        Recalculate(roundOff);
    }

    public void SetCustomer(string? name, string? phone, string? gstin)
    {
        EnsurePending("changed");
        CustomerName = Clean(name);
        CustomerPhone = Clean(phone);
        CustomerGstin = Clean(gstin)?.ToUpperInvariant();
    }

    public void Finalize(string invoiceNumber, int userId, DateTime nowUtc)
    {
        EnsureOpen("finalized");
        Status = BillStatus.Finalized;
        InvoiceNumber = invoiceNumber;
        FinalizedAt = nowUtc;
        if (GrandTotal == 0)
        {
            Settle(userId, nowUtc);
        }
    }

    public Payment AddPayment(
        PaymentMethod method,
        decimal amount,
        decimal? tendered,
        string? reference,
        int userId,
        Guid? deviceId,
        Guid idempotencyKey,
        DateTime nowUtc)
    {
        if (Status == BillStatus.Open)
        {
            throw new DomainException("Finalize the bill before taking payment.", ErrorCodes.InvalidStateTransition);
        }

        EnsurePending("paid");
        if (!method.IsActive)
        {
            throw new DomainException($"Payment method {method.Name} is no longer active.");
        }

        if (amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("Enter an amount above zero with at most two decimals.", ErrorCodes.ValidationError);
        }

        var cleanReference = Clean(reference);
        if (method.RequiresReference && cleanReference is null)
        {
            throw new DomainException($"Enter the {method.Name} reference number.", ErrorCodes.ValidationError);
        }

        var balance = BalanceDue;
        decimal applied;
        decimal? tenderedAmount = null;
        decimal? change = null;
        if (method.IsCash)
        {
            tenderedAmount = tendered ?? amount;
            if (tenderedAmount < amount)
            {
                throw new DomainException("The cash tendered is less than the amount.", ErrorCodes.ValidationError);
            }

            applied = Math.Min(amount, balance);
            change = tenderedAmount - applied;
        }
        else
        {
            if (amount > balance)
            {
                throw new DomainException(
                    $"{method.Name} payment of {amount:0.00} is more than the balance due of {balance:0.00}.",
                    ErrorCodes.PaymentExceedsBalance);
            }

            applied = amount;
        }

        var payment = new Payment(method.Id, applied, tenderedAmount, change, cleanReference, userId, deviceId, idempotencyKey, nowUtc);
        _payments.Add(payment);
        PaidAmount += applied;
        if (PaidAmount >= GrandTotal)
        {
            Settle(userId, nowUtc);
        }
        else
        {
            PaymentStatus = PaymentStatus.PartiallyPaid;
        }

        return payment;
    }

    public void Void(string reason, DateTime nowUtc)
    {
        if (Status != BillStatus.Finalized)
        {
            throw new DomainException(
                Status == BillStatus.Open
                    ? "Only a finalized bill can be voided. Reopen an open bill instead."
                    : $"Bill {BillNumber} is {Status} and cannot be voided.",
                ErrorCodes.InvalidStateTransition);
        }

        CancelUnpaid(reason, nowUtc);
    }

    public void Reopen(string reason, DateTime nowUtc)
    {
        EnsurePending("reopened");
        CancelUnpaid(reason, nowUtc);
    }

    public Payment Refund(int paymentId, decimal amount, string reason, int userId, Guid? deviceId, Guid idempotencyKey, DateTime nowUtc)
    {
        if (Status != BillStatus.Settled)
        {
            throw new DomainException("Only a settled bill can be refunded.", ErrorCodes.InvalidStateTransition);
        }

        var original = _payments.FirstOrDefault(p => p.Id == paymentId && !p.IsRefund)
            ?? throw new DomainException("That payment does not belong to this bill.", ErrorCodes.NotFound);
        var refundable = RefundableAmount(original);
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("Enter a refund amount above zero with at most two decimals.", ErrorCodes.ValidationError);
        }

        if (amount > refundable)
        {
            throw new DomainException($"At most {refundable:0.00} of this payment can still be refunded.");
        }

        var cleanReason = Clean(reason) ?? throw new DomainException("Enter the reason for the refund.", ErrorCodes.ValidationError);
        var refund = Payment.RefundOf(original, amount, cleanReason, userId, deviceId, idempotencyKey, nowUtc);
        _payments.Add(refund);
        RefundedAmount += amount;
        if (amount == refundable)
        {
            original.MarkRefunded();
        }

        if (RefundedAmount >= PaidAmount)
        {
            PaymentStatus = PaymentStatus.Refunded;
        }

        return refund;
    }

    public decimal RefundableAmount(Payment payment) =>
        payment.IsRefund ? 0m : payment.Amount + _payments.Where(p => p.RefundOfPaymentId == payment.Id).Sum(p => p.Amount);

    private void Settle(int userId, DateTime nowUtc)
    {
        Status = BillStatus.Settled;
        PaymentStatus = PaymentStatus.Paid;
        SettledAt = nowUtc;
        SettledBy = userId;
        Release();
    }

    private void CancelUnpaid(string reason, DateTime nowUtc)
    {
        if (PaidAmount > 0)
        {
            throw new DomainException("Payments were already taken on this bill. Refund them instead.");
        }

        Status = BillStatus.Voided;
        PaymentStatus = PaymentStatus.Cancelled;
        VoidedAt = nowUtc;
        VoidReason = Clean(reason);
        Release();
    }

    private void Recalculate(bool roundOff)
    {
        var discount = DiscountType is { } type && DiscountValue is { } value ? new BillDiscountInput(type, value) : null;
        var result = BillCalculator.Calculate(_items.Select(i => i.ToInput()).ToList(), discount, roundOff);
        for (var i = 0; i < _items.Count; i++)
        {
            _items[i].Apply(result.Lines[i]);
        }

        Subtotal = result.Subtotal;
        DiscountAmount = result.DiscountAmount;
        TaxableAmount = result.TaxableAmount;
        TaxAmount = result.TaxAmount;
        RoundOff = result.RoundOff;
        GrandTotal = result.GrandTotal;
    }

    private void EnsureOpen(string action)
    {
        if (Status != BillStatus.Open)
        {
            throw new DomainException(
                Status == BillStatus.Finalized
                    ? $"Bill {BillNumber} is finalized and cannot be {action}. Reopen it first."
                    : $"Bill {BillNumber} is {Status} and cannot be {action}.",
                ErrorCodes.InvalidStateTransition);
        }
    }

    private void EnsurePending(string action)
    {
        if (!IsPending)
        {
            throw new DomainException($"Bill {BillNumber} is {Status} and cannot be {action}.", ErrorCodes.InvalidStateTransition);
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

public sealed class BillItem
{
    private BillItem()
    {
    }

    internal BillItem(BillLineInput line)
    {
        OrderItemId = line.OrderItemId;
        ItemName = line.ItemName;
        Quantity = line.Quantity;
        UnitPrice = line.UnitPrice;
        ModifiersAmount = line.ModifiersAmount;
        TaxRatePercent = line.TaxRatePercent;
    }

    public int Id { get; private set; }
    public int BillId { get; private set; }
    public int OrderItemId { get; private set; }
    public string ItemName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    // Sum of the modifier price deltas for one unit.
    public decimal ModifiersAmount { get; private set; }

    public decimal LineSubtotal { get; private set; }
    public decimal DiscountShare { get; private set; }
    public decimal TaxRatePercent { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal LineTotal { get; private set; }

    internal BillLineInput ToInput() => new(OrderItemId, ItemName, Quantity, UnitPrice, ModifiersAmount, TaxRatePercent);

    internal void Apply(BillLineResult result)
    {
        LineSubtotal = result.LineSubtotal;
        DiscountShare = result.DiscountShare;
        TaxAmount = result.TaxAmount;
        LineTotal = result.LineTotal;
    }
}

public sealed class Payment : BaseEntity
{
    private Payment()
    {
    }

    internal Payment(
        int paymentMethodId,
        decimal amount,
        decimal? tenderedAmount,
        decimal? changeAmount,
        string? reference,
        int receivedBy,
        Guid? deviceId,
        Guid idempotencyKey,
        DateTime paidAtUtc)
    {
        PaymentMethodId = paymentMethodId;
        Amount = amount;
        TenderedAmount = tenderedAmount;
        ChangeAmount = changeAmount;
        Reference = reference;
        Status = PaymentRecordStatus.Completed;
        ReceivedBy = receivedBy;
        DeviceId = deviceId;
        IdempotencyKey = idempotencyKey;
        PaidAt = paidAtUtc;
    }

    public int BillId { get; private set; }
    public int PaymentMethodId { get; private set; }

    // Refund rows carry a negative amount and point at the payment they return.
    public decimal Amount { get; private set; }

    public decimal? TenderedAmount { get; private set; }
    public decimal? ChangeAmount { get; private set; }
    public string? Reference { get; private set; }
    public PaymentRecordStatus Status { get; private set; }
    public int ReceivedBy { get; private set; }
    public Guid? DeviceId { get; private set; }
    public DateTime PaidAt { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public int? RefundOfPaymentId { get; private set; }
    public string? RefundReason { get; private set; }

    public bool IsRefund => RefundOfPaymentId is not null;

    internal static Payment RefundOf(Payment original, decimal amount, string reason, int userId, Guid? deviceId, Guid idempotencyKey, DateTime nowUtc) =>
        new(original.PaymentMethodId, -amount, null, null, original.Reference, userId, deviceId, idempotencyKey, nowUtc)
        {
            RefundOfPaymentId = original.Id,
            RefundReason = reason,
        };

    internal void MarkRefunded() => Status = PaymentRecordStatus.Refunded;
}
