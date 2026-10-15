using HotelPOS.Application.Features.Billing.Commands;
using HotelPOS.Application.Features.Customers.Commands;
using HotelPOS.Application.Features.Customers.Queries;
using HotelPOS.Application.Tests.Gst;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Customers;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Tests.Customers;

public class CustomerTests : GstTestBase
{
    public CustomerTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    private async Task<CustomerDto> CreateAsync(string name, string? phone = null, string? gstin = null)
    {
        var created = await SendAsync(new CreateCustomerCommand(new SaveCustomerRequest { Name = name, Phone = phone, Gstin = gstin }));
        created.IsSuccess.Should().BeTrue(created.Error?.Message);
        return created.Value;
    }

    private async Task<IReadOnlyList<string>> SearchAsync(string? text, bool includeInactive = false, int pageSize = 50) =>
        (await SendAsync(new ListCustomersQuery(new CustomerQuery { Search = text, IncludeInactive = includeInactive, PageSize = pageSize }))).Value.Items.Select(c => c.Name).ToList();

    [Fact]
    public async Task Search_FindsByNamePhoneOrGstin_AndHidesDeactivatedCustomers()
    {
        var v = await GstVenueAsync();
        await CreateAsync("Asha Traders", "+91 98765 43210", BuyerGstin);
        var ravi = await CreateAsync("Ravi Kumar", "98450 11223");
        await CreateAsync("Meena Stores");

        (await SearchAsync("asha")).Should().Equal("Asha Traders");
        (await SearchAsync("98765")).Should().Equal("Asha Traders");
        (await SearchAsync("+91 9845011223")).Should().Equal("Ravi Kumar");
        (await SearchAsync(BuyerGstin[..8].ToLowerInvariant())).Should().Equal("Asha Traders");
        (await SearchAsync(null)).Should().Equal("Asha Traders", "Meena Stores", "Ravi Kumar");
        (await SearchAsync(null, pageSize: 2)).Should().HaveCount(2);

        (await SendAsync(new SetCustomerActiveCommand(ravi.Id, false))).Value.IsActive.Should().BeFalse();
        (await SearchAsync("ravi")).Should().BeEmpty();
        (await SearchAsync("ravi", includeInactive: true)).Should().Equal("Ravi Kumar");
        (await SendAsync(new SetCustomerActiveCommand(ravi.Id, true))).Value.IsActive.Should().BeTrue();
        (await SearchAsync("ravi")).Should().Equal("Ravi Kumar");
        _ = v;
    }

    [Fact]
    public async Task CreateAndUpdate_NormalisePhone_AndRefuseADuplicatePhone()
    {
        await GstVenueAsync();
        var asha = await CreateAsync("Asha Traders", "+91 98765 43210");
        asha.Phone.Should().Be("9876543210");

        var duplicate = await SendAsync(new CreateCustomerCommand(new SaveCustomerRequest { Name = "Other", Phone = "09876543210" }));
        duplicate.Error!.Code.Should().Be(ErrorCodes.Duplicate);

        var ravi = await CreateAsync("Ravi", "9845011223");
        var clash = await SendAsync(new UpdateCustomerCommand(ravi.Id, new SaveCustomerRequest { Name = "Ravi", Phone = "9876543210" }));
        clash.Error!.Code.Should().Be(ErrorCodes.Duplicate);

        var renamed = await SendAsync(new UpdateCustomerCommand(ravi.Id, new SaveCustomerRequest { Name = "Ravi K", Phone = "9845011223", Address = "MG Road" }));
        (renamed.Value.Name, renamed.Value.Address).Should().Be(("Ravi K", "MG Road"));
        (await SendAsync(new UpdateCustomerCommand(9999, new SaveCustomerRequest { Name = "x" }))).Error!.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Create_RejectsAnInvalidGstin()
    {
        await GstVenueAsync();

        var result = await SendAsync(new CreateCustomerCommand(new SaveCustomerRequest { Name = "Bad", Gstin = "33AABCT1332L1Z0" }));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task AttachingAPickedCustomer_CopiesItsDetailsOntoTheBill_AndFillsTheGstDetails()
    {
        var v = await GstVenueAsync();
        var asha = await CreateAsync("Asha Traders", "9876543210", BuyerGstin);
        var bill = await BillAsync(v, v.Tables[0]);

        var attached = await SendAsync(new SetBillCustomerCommand(bill.Id, new UpdateCustomerRequest { CustomerId = asha.Id, RowVersion = bill.RowVersion }));

        attached.IsSuccess.Should().BeTrue(attached.Error?.Message);
        (attached.Value.CustomerName, attached.Value.CustomerPhone, attached.Value.CustomerGstin).Should().Be(("Asha Traders", "9876543210", BuyerGstin));
        attached.Value.CustomerStateCode.Should().Be(BuyerGstin[..2]);

        var cleared = await SendAsync(new SetBillCustomerCommand(bill.Id, new UpdateCustomerRequest { RowVersion = attached.Value.RowVersion }));
        (cleared.Value.CustomerName, cleared.Value.CustomerGstin).Should().Be((null, null));
    }

    [Fact]
    public async Task AttachingADeactivatedOrUnknownCustomer_IsRefused()
    {
        var v = await GstVenueAsync();
        var gone = await CreateAsync("Gone Ltd", "9000000001");
        await SendAsync(new SetCustomerActiveCommand(gone.Id, false));
        var bill = await BillAsync(v, v.Tables[0]);

        var inactive = await SendAsync(new SetBillCustomerCommand(bill.Id, new UpdateCustomerRequest { CustomerId = gone.Id, RowVersion = bill.RowVersion }));
        var unknown = await SendAsync(new SetBillCustomerCommand(bill.Id, new UpdateCustomerRequest { CustomerId = 424242, RowVersion = bill.RowVersion }));

        inactive.Error!.Code.Should().Be(ErrorCodes.NotFound);
        unknown.Error!.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task OnlyTheBillingDesk_MayUseTheCustomerList()
    {
        var v = await GstVenueAsync();
        CurrentUser.SignIn(v.Waiter, "waiter", Roles.Waiter);

        var result = await SendAsync(new ListCustomersQuery(new CustomerQuery()));

        result.Error!.Code.Should().Be(ErrorCodes.Forbidden);
    }
}
