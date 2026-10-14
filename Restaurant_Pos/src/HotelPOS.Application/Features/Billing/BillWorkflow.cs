using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Floor;
using HotelPOS.Application.Features.Kitchen;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing;

// The common frame of the bill commands (load, claim, version check, approval, change, save) and the finalize and
// settle steps that several of them share. Bill, order, table and tickets change in the command's transaction;
// events are queued for after the commit.
internal sealed class BillWorkflow
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IInvoiceNumberService _invoices;
    private readonly KitchenSync _kitchen;
    private readonly TableEvents _tableEvents;
    private readonly BillSteps _bills;
    private readonly BillEvents _events;

    public BillWorkflow(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        ICurrentUser currentUser,
        IClock clock,
        IInvoiceNumberService invoices,
        KitchenSync kitchen,
        TableEvents tableEvents,
        BillSteps bills,
        BillEvents events)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _currentUser = currentUser;
        _clock = clock;
        _invoices = invoices;
        _kitchen = kitchen;
        _tableEvents = tableEvents;
        _bills = bills;
        _events = events;
    }

    // prepare runs before anything changes (approvals); step changes the bill and may return a business error.
    public async Task<Result<BillDetailDto>> RunAsync(
        int id,
        string? rowVersion,
        bool claim,
        Func<BillScope, Task<AppError?>>? prepare,
        Func<BillScope, Task<AppError?>> step,
        CancellationToken cancellationToken)
    {
        var scope = await _bills.LoadAsync(id, cancellationToken);
        if (scope is null)
        {
            return AppErrors.NotFound("Bill", id);
        }

        var bill = scope.Bill;
        var now = _clock.UtcNow;
        if (claim && bill.IsPending && bill.IsHeldByOther(_bills.UserId, _currentUser.DeviceId, now))
        {
            return AppErrors.BillClaimed(await _bills.HolderAsync(bill, cancellationToken), await _bills.DetailAsync(id, cancellationToken));
        }

        if (rowVersion is not null && !RowVersions.Matches(bill.RowVersion, rowVersion))
        {
            return AppErrors.Concurrency("bill", await _bills.DetailAsync(id, cancellationToken));
        }

        if (prepare is not null && await prepare(scope) is { } rejected)
        {
            return rejected;
        }

        try
        {
            if (claim && bill.IsPending)
            {
                bill.Claim(_bills.UserId, _currentUser.DeviceId, now);
            }

            if (await step(scope) is { } error)
            {
                return error;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message)
            {
                Data = ex.Code == ErrorCodes.InvalidStateTransition ? await _bills.DetailAsync(id, cancellationToken) : null,
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppErrors.Concurrency("bill", await _bills.DetailAsync(id, cancellationToken));
        }

        return await _bills.GetAsync(id, cancellationToken);
    }

    public async Task FinalizeAsync(BillScope scope, CancellationToken cancellationToken)
    {
        var (bill, order) = (scope.Bill, scope.Order);
        if (bill.Status != BillStatus.Open)
        {
            throw new DomainException($"Bill {bill.BillNumber} is already {bill.Status}.", ErrorCodes.InvalidStateTransition);
        }

        var now = _clock.UtcNow;
        bill.Finalize(await _invoices.NextAsync(now, cancellationToken), _bills.UserId, now);
        order.MarkBilled();
        _audit.Add(AuditActions.BillFinalized, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture),
            newValues: new { bill.BillNumber, bill.InvoiceNumber, bill.GrandTotal, order.OrderNumber });
        _afterCommit.Enqueue(() => _events.PublishOrderUpdatedAsync(order, OrderChangeTypes.Billed));
        if (bill.Status == BillStatus.Settled)
        {
            await SettleAsync(scope, cancellationToken);
        }
        else
        {
            _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.Finalized));
        }
    }

    public async Task SettleAsync(BillScope scope, CancellationToken cancellationToken)
    {
        var (bill, order, table) = (scope.Bill, scope.Order, scope.Table);
        var now = _clock.UtcNow;
        order.MarkPaid();
        if (await _db.BoolSettingAsync(SettingKeys.AutoCloseOnFullPayment, true, cancellationToken))
        {
            order.Close(now);
        }

        var closed = (await _kitchen.LoadTicketsAsync(order.Id, cancellationToken)).Where(t => t.IsOpen).ToList();
        foreach (var ticket in closed)
        {
            ticket.CloseOnSettle(_bills.UserId, now);
        }

        if (table.CurrentOrderId == order.Id)
        {
            table.DetachOrder(keepGuestsSeated: false);
        }

        _audit.Add(AuditActions.BillSettled, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new
        {
            bill.InvoiceNumber,
            bill.GrandTotal,
            bill.PaidAmount,
            Payments = bill.Payments.Count,
            order.OrderNumber,
            OrderStatus = order.Status,
        });

        _afterCommit.Enqueue(() => _events.PublishPaymentCompletedAsync(bill, order, table));
        _afterCommit.Enqueue(() => _tableEvents.PublishStatusAsync(table));
        _afterCommit.Enqueue(() => _events.PublishOrderUpdatedAsync(order, order.Status == OrderStatus.Completed ? OrderChangeTypes.Completed : OrderChangeTypes.Paid));
        _afterCommit.Enqueue(() => _events.PublishTicketsAsync(order, table, closed));
    }
}
