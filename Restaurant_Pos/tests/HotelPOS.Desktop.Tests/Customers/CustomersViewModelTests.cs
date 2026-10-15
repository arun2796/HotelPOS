using System.Net;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Customers;
using HotelPOS.Desktop.Modules.Customers;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Ui;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Customers;

public sealed class CustomersViewModelTests
{
    private readonly ICustomersApi _api = Substitute.For<ICustomersApi>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly List<CustomerDto> _customers = new()
    {
        new() { Id = 1, Name = "Asha Traders", Phone = "9876543210", Gstin = "27AAPFU0939F1ZV", IsActive = true },
        new() { Id = 2, Name = "Ravi Kumar", IsActive = true },
    };

    private CustomersViewModel Create()
    {
        _api.SearchAsync(Arg.Any<CustomerQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var query = call.Arg<CustomerQuery>();
            var items = _customers.Where(c => (query.IncludeInactive || c.IsActive)
                && (query.Search is null || c.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase))).ToList();
            return ApiResult<PagedResult<CustomerDto>>.Ok(new PagedResult<CustomerDto> { Items = items, Page = 1, PageSize = 25, TotalCount = items.Count });
        });
        return new CustomersViewModel(_api, _dialogs, _notifications);
    }

    [Fact]
    public async Task Loads_Searches_AndHidesInactiveCustomersUntilAsked()
    {
        var vm = Create();
        await vm.OnNavigatedToAsync(null);
        vm.Customers.Should().HaveCount(2);

        vm.Search = "ravi";
        await vm.RunSearchCommand.ExecuteAsync(null);
        vm.Customers.Should().ContainSingle().Which.Name.Should().Be("Ravi Kumar");

        _customers[1] = _customers[1] with { IsActive = false };
        await vm.RunSearchCommand.ExecuteAsync(null);
        vm.Customers.Should().BeEmpty();
        vm.ShowInactive = true;
        await Task.Delay(100);
        vm.Customers.Should().ContainSingle();
    }

    [Fact]
    public async Task Add_ChecksTheForm_ThenCreatesAndSelectsTheCustomer()
    {
        var vm = Create();
        await vm.OnNavigatedToAsync(null);
        SaveCustomerRequest? sent = null;
        _api.CreateAsync(Arg.Do<SaveCustomerRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(_ => ApiResult<CustomerDto>.Ok(new CustomerDto { Id = 3, Name = sent!.Name, IsActive = true }));

        vm.NewCustomerCommand.Execute(null);
        vm.Editor.IsOpen.Should().BeTrue();
        await vm.SaveCustomerCommand.ExecuteAsync(null);
        vm.Editor.ErrorMessage.Should().Be("Enter the name of the customer.");

        vm.Editor.Name = "  Meena Stores ";
        vm.Editor.Gstin = "27AAPFU0939F1ZA";
        await vm.SaveCustomerCommand.ExecuteAsync(null);
        vm.Editor.ErrorMessage.Should().Contain("check character");

        vm.Editor.Gstin = "27AAPFU0939F1ZV";
        vm.Editor.StateCode.Should().Be("27");
        await vm.SaveCustomerCommand.ExecuteAsync(null);

        sent!.Name.Should().Be("Meena Stores");
        vm.Editor.IsOpen.Should().BeFalse();
        _notifications.Received(1).Success("Meena Stores added.");
    }

    [Fact]
    public async Task Edit_PrefillsTheForm_AndShowsTheServerMessageOnADuplicatePhone()
    {
        var vm = Create();
        await vm.OnNavigatedToAsync(null);
        vm.SelectedCustomer = vm.Customers[0];
        _api.UpdateAsync(1, Arg.Any<SaveCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<CustomerDto>.Fail(ErrorCodes.Duplicate, "Ravi Kumar already has the phone number 9876543210.", HttpStatusCode.Conflict));

        vm.EditCommand.Execute(null);
        (vm.Editor.IsNew, vm.Editor.Name, vm.Editor.Phone, vm.Editor.Gstin).Should().Be((false, "Asha Traders", "9876543210", "27AAPFU0939F1ZV"));
        await vm.SaveCustomerCommand.ExecuteAsync(null);

        vm.Editor.IsOpen.Should().BeTrue();
        vm.Editor.ErrorMessage.Should().Contain("already has the phone number");
    }

    [Fact]
    public async Task Deactivate_AsksFirst_ThenHidesTheCustomer()
    {
        var vm = Create();
        await vm.OnNavigatedToAsync(null);
        vm.SelectedCustomer = vm.Customers[1];
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
        _api.SetActiveAsync(2, false, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _customers[1] = _customers[1] with { IsActive = false };
            return ApiResult<CustomerDto>.Ok(_customers[1]);
        });

        await vm.ToggleActiveCommand.ExecuteAsync(null);

        await _api.Received(1).SetActiveAsync(2, false, Arg.Any<CancellationToken>());
        vm.Customers.Should().ContainSingle().Which.Name.Should().Be("Asha Traders");
    }
}
