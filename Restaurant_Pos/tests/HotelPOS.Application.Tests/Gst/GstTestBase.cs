using HotelPOS.Application.Features.Billing.Commands;
using HotelPOS.Application.Features.DayEnd.Commands;
using HotelPOS.Application.Tests.Cash;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Gst;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Gst;

// A Tamil Nadu restaurant (GSTIN state 33) trading on 7 October 2026, with a 5 % biryani at Rs 250.
public abstract class GstTestBase : CashTestBase
{
    protected static readonly string RestaurantGstin = Gstin.Normalize(WithCheck("33AAPFU0939F1Z"));
    protected static readonly string BuyerGstin = Gstin.Normalize(WithCheck("33AABCT1332L1Z"));
    protected static readonly string FarBuyerGstin = Gstin.Normalize(WithCheck("27AAPFU0939F1Z"));

    protected static string WithCheck(string first14) => first14 + Gstin.CheckCharacter(first14);

    protected GstTestBase(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    protected static DateOnly TradeDay => new(2026, 10, 7);

    protected async Task<Venue> GstVenueAsync()
    {
        var v = await VenueAsync(4);
        await SetSettingAsync(SettingKeys.Gstin, RestaurantGstin);
        AsManager(v);
        return v;
    }

    // Bills the table, optionally sets the customer, and pays the whole bill by card (cards need no cash shift).
    protected async Task<BillDetailDto> SettleAsync(Venue v, int table, int quantity = 2, UpdateCustomerRequest? customer = null)
    {
        var bill = await BillAsync(v, table, quantity);
        await CompleteKitchenAsync(v, bill.OrderId);
        AsManager(v);
        if (customer is not null)
        {
            var set = await SendAsync(new SetBillCustomerCommand(bill.Id, customer with { RowVersion = bill.RowVersion }));
            set.IsSuccess.Should().BeTrue(set.Error?.Message);
            bill = set.Value;
        }

        var paid = await SendAsync(new AddPaymentCommand(bill.Id, Card(bill, bill.GrandTotal), Guid.NewGuid()));
        paid.IsSuccess.Should().BeTrue(paid.Error?.Message);
        return paid.Value;
    }

    protected async Task CloseTradeDayAsync()
    {
        var overrides = DayEndCheckCodes.All.Select(c => new DayEndOverrideDto { Code = c, Reason = "Test override" }).ToArray();
        (await SendAsync(new CloseBusinessDayCommand(TradeDay, overrides, "test.dump"))).IsSuccess.Should().BeTrue();
    }

    protected void AsAdmin(Venue v) => CurrentUser.SignIn(v.Admin, "admin", Roles.Admin);

    // The month after the trade day has started, so October 2026 is over.
    protected void MoveToNovember() => Clock.UtcNow = new DateTime(2026, 11, 3, 6, 30, 0, DateTimeKind.Utc);
}
