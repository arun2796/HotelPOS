using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Print;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Print.Queries;

// A payment or refund slip. The cashier desk: the endpoint gates by role, there is no permission.
public sealed record GetReceiptDocumentQuery(int PaymentId) : IQuery<ReceiptDocument>;

internal sealed class GetReceiptDocumentHandler : IQueryHandler<GetReceiptDocumentQuery, ReceiptDocument>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly PrintSources _sources;

    public GetReceiptDocumentHandler(IAppDbContext db, IClock clock, PrintSources sources)
    {
        _db = db;
        _clock = clock;
        _sources = sources;
    }

    public async Task<Result<ReceiptDocument>> Handle(GetReceiptDocumentQuery request, CancellationToken cancellationToken)
    {
        var paymentId = request.PaymentId;
        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return AppErrors.NotFound("Payment", paymentId);
        }

        var bill = await _db.Bills.AsNoTracking().FirstAsync(b => b.Id == payment.BillId, cancellationToken);
        var orderNumber = await _db.Orders.AsNoTracking().Where(o => o.Id == bill.OrderId).Select(o => o.OrderNumber).FirstAsync(cancellationToken);
        var settings = await _sources.SettingsAsync(cancellationToken);
        var method = await _db.PaymentMethods.AsNoTracking().Where(m => m.Id == payment.PaymentMethodId).Select(m => m.Name).FirstOrDefaultAsync(cancellationToken);

        return new ReceiptDocument
        {
            PaymentId = payment.Id,
            BillId = bill.Id,
            InvoiceNumber = bill.InvoiceNumber,
            RestaurantName = settings.GetValueOrDefault(SettingKeys.RestaurantName) ?? string.Empty,
            CurrencySymbol = settings.GetValueOrDefault(SettingKeys.CurrencySymbol) ?? string.Empty,
            TableCode = bill.TableCode,
            OrderNumber = orderNumber,
            Method = method ?? string.Empty,
            Amount = Math.Abs(payment.Amount),
            TenderedAmount = payment.TenderedAmount,
            ChangeAmount = payment.ChangeAmount,
            Reference = payment.Reference,
            IsRefund = payment.IsRefund,
            RefundReason = payment.RefundReason,
            PaidAtUtc = payment.PaidAt,
            PrintedAtUtc = _clock.UtcNow,
            ReceivedBy = await _sources.UserNameAsync(payment.ReceivedBy, cancellationToken),
            GrandTotal = bill.GrandTotal,
            PaidAmount = bill.PaidAmount - bill.RefundedAmount,
            BalanceDue = bill.BalanceDue,
            Footer = PrintSources.Blank(settings.GetValueOrDefault(SettingKeys.ReceiptFooter)),
        };
    }
}
