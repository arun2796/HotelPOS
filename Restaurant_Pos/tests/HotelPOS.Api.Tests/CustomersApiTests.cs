using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Customers;
using HotelPOS.Contracts.Gst;

namespace HotelPOS.Api.Tests;

public class CustomersApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CustomersApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Customers_OverHttp_CreateSearchAttachToABillAndDeactivate()
    {
        var cashier = await _factory.CreateTerminalClientAsync("cashier1", ApiFactory.DemoPassword, "BILLING-01");
        var gstin = "33AABCT1332L1Z" + Gstin.CheckCharacter("33AABCT1332L1Z");

        var created = await cashier.PostAsJsonAsync("/api/customers", new SaveCustomerRequest { Name = "Asha Traders", Phone = "+91 98765 43210", Gstin = gstin }, PosJson.Options);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var customer = (await created.ReadEnvelopeAsync<CustomerDto>()).Data!;
        customer.Phone.Should().Be("9876543210");

        (await cashier.PostAsJsonAsync("/api/customers", new SaveCustomerRequest { Name = "Same phone", Phone = "9876543210" }, PosJson.Options))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var found = (await (await cashier.GetAsync("/api/customers?search=98765&pageSize=8")).ReadEnvelopeAsync<PagedResult<CustomerDto>>()).Data!;
        found.Items.Should().ContainSingle().Which.Id.Should().Be(customer.Id);

        var bill = await ApiScenario.BillForAsync(_factory, "T01");
        var attach = await cashier.PutAsJsonAsync($"/api/billing/{bill.Id}/customer", new UpdateCustomerRequest { CustomerId = customer.Id, RowVersion = bill.RowVersion }, PosJson.Options);
        attach.StatusCode.Should().Be(HttpStatusCode.OK);
        var attached = (await attach.ReadEnvelopeAsync<BillDetailDto>()).Data!;
        (attached.CustomerName, attached.CustomerGstin).Should().Be(("Asha Traders", gstin));

        (await cashier.PostAsync($"/api/customers/{customer.Id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await cashier.GetAsync("/api/customers?search=asha")).ReadEnvelopeAsync<PagedResult<CustomerDto>>()).Data!.Items.Should().BeEmpty();
        var refused = await cashier.PutAsJsonAsync($"/api/billing/{bill.Id}/customer", new UpdateCustomerRequest { CustomerId = customer.Id, RowVersion = attached.RowVersion }, PosJson.Options);
        refused.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customers_AreForbiddenToWaitersAndKitchen()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);

        (await waiter.GetAsync("/api/customers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
