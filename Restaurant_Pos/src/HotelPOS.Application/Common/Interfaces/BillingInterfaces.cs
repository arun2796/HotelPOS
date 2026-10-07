namespace HotelPOS.Application.Common.Interfaces;

public interface IInvoiceNumberService
{
    // Must run inside the caller's transaction: the counter row stays locked until it commits or rolls back.
    Task<string> NextAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
