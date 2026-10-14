using FluentAssertions;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using HotelPOS.Desktop.Modules.Admin;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using NSubstitute;
using Xunit;

namespace HotelPOS.Desktop.Tests;

public sealed class SecurityViewModelTests
{
    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }

    [Fact]
    public async Task RolesViewModel_LoadsRoles_ProtectsAdmin_AndSavesCustomPermissions()
    {
        var usersApi = Substitute.For<IUsersApi>();
        var dialogs = Substitute.For<IDialogService>();
        var notifications = Substitute.For<INotificationService>();

        var adminRole = new RolePermissionsDto
        {
            RoleId = 1,
            RoleName = "Admin",
            IsSystem = true,
            Permissions = Permissions.All,
        };
        var cashierRole = new RolePermissionsDto
        {
            RoleId = 2,
            RoleName = "Cashier",
            IsSystem = false,
            Permissions = new[] { Permissions.OrdersCreate, Permissions.BillingOperate },
        };

        usersApi.ListAllRolePermissionsAsync(Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<RolePermissionsDto>>.Ok(new List<RolePermissionsDto> { adminRole, cashierRole }));

        usersApi.UpdateRolePermissionsAsync(2, Arg.Any<UpdateRolePermissionsRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<object>.Ok(new object()));

        dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(true);

        var vm = new RolesViewModel(usersApi, dialogs, notifications);
        await vm.OnNavigatedToAsync(null);

        vm.Roles.Should().HaveCount(2);

        // Selecting Admin
        vm.SelectedRole = adminRole;
        vm.IsAdminRole.Should().BeTrue();
        vm.CanEdit.Should().BeFalse();
        vm.PermissionItems.All(p => p.IsGranted && p.IsReadOnly).Should().BeTrue();

        // Selecting Cashier
        vm.SelectedRole = cashierRole;
        vm.IsAdminRole.Should().BeFalse();
        vm.CanEdit.Should().BeTrue();

        var ordersItem = vm.PermissionItems.First(p => p.Key == Permissions.OrdersCreate);
        ordersItem.IsGranted.Should().BeTrue();
        ordersItem.IsReadOnly.Should().BeFalse();

        // Toggle a permission and save
        var kitchenItem = vm.PermissionItems.First(p => p.Key == Permissions.KitchenOperate);
        kitchenItem.IsGranted = true;

        vm.ForceRelogin = true;
        await vm.SavePermissionsCommand.ExecuteAsync(null);

        await usersApi.Received(1).UpdateRolePermissionsAsync(2, Arg.Is<UpdateRolePermissionsRequest>(r =>
            r.ForceRelogin && r.Permissions.Contains(Permissions.KitchenOperate) && r.Permissions.Contains(Permissions.OrdersCreate)));
    }

    [Fact]
    public async Task DevicesViewModel_Loads_TogglesActive_AndHandlesRealtimeStatusUpdate()
    {
        var devicesApi = Substitute.For<IDevicesApi>();
        var dialogs = Substitute.For<IDialogService>();
        var notifications = Substitute.For<INotificationService>();
        var realtime = Substitute.For<IRealtimeClient>();
        var dispatcher = new ImmediateDispatcher();

        var deviceId = Guid.NewGuid();
        var device = new DeviceDto
        {
            Id = deviceId,
            Name = "Register 1",
            IsActive = true,
            IsOnline = false,
            RegisteredAtUtc = DateTime.UtcNow.AddDays(-10),
        };

        Action<DeviceStatusChangedEvent>? realtimeHandler = null;
        realtime.Subscribe<DeviceStatusChangedEvent>(HubEvents.DeviceStatusChanged, Arg.Do<Action<DeviceStatusChangedEvent>>(h => realtimeHandler = h))
            .Returns(Substitute.For<IDisposable>());

        devicesApi.ListAsync(Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<DeviceDto>>.Ok(new List<DeviceDto> { device }));

        // The server answers (de)activate and update without data; that is still a success.
        devicesApi.DeactivateAsync(deviceId, Arg.Any<CancellationToken>())
            .Returns(ApiResult<DeviceDto>.Ok(null));
        devicesApi.UpdateAsync(deviceId, Arg.Any<UpdateDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<DeviceDto>.Ok(null));

        dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(true);

        var menuApi = Substitute.For<IMenuApi>();
        menuApi.GetStationsAsync(false, Arg.Any<CancellationToken>()).Returns(ApiResult<List<StationDto>>.Ok(new List<StationDto>
        {
            new() { Id = 2, Name = "Bar", SortOrder = 2, IsActive = true },
        }));
        var vm = new DevicesViewModel(devicesApi, menuApi, dialogs, notifications, realtime, dispatcher);
        await vm.OnNavigatedToAsync(null);

        vm.Devices.Should().HaveCount(1);
        vm.SelectedDevice = vm.Devices[0];
        vm.ToggleActiveText.Should().Be("Deactivate");

        // Deactivate device
        await vm.ToggleActiveCommand.ExecuteAsync(null);
        await devicesApi.Received(1).DeactivateAsync(deviceId);

        // Realtime event: device goes online
        realtimeHandler.Should().NotBeNull();
        var onlineEvt = new DeviceStatusChangedEvent { DeviceId = deviceId, DeviceName = "Register 1", IsOnline = true, IsActive = true, TimestampUtc = DateTime.UtcNow };
        realtimeHandler!(onlineEvt);

        vm.Devices[0].IsOnline.Should().BeTrue();
        notifications.Received(1).Success(Arg.Is<string>(m => m.Contains("deactivated")));
        notifications.DidNotReceiveWithAnyArgs().Error(default!, default);

        // Assign the kitchen display to the Bar station.
        vm.SelectedDevice = vm.Devices[0];
        vm.AssignStationCommand.CanExecute(null).Should().BeFalse("the device already shows every station");
        vm.SelectedStation = vm.Stations.Single(s => s.Name == "Bar");
        await vm.AssignStationCommand.ExecuteAsync(null);
        await devicesApi.Received(1).UpdateAsync(deviceId, Arg.Is<UpdateDeviceRequest>(r => r.StationId == 2 && r.Name == "Register 1"), Arg.Any<CancellationToken>());

        // A rename arrives through the presence event; "last seen" moves only on an online/offline edge.
        var seenAt = vm.Devices[0].LastSeenAtUtc;
        realtimeHandler!(onlineEvt with { DeviceName = "REGISTER-1", IsOnline = false, TimestampUtc = DateTime.UtcNow.AddMinutes(5) });
        vm.Devices[0].Name.Should().Be("REGISTER-1");
        vm.Devices[0].LastSeenAtUtc.Should().Be(seenAt);
    }

    [Fact]
    public async Task AuditLogsViewModel_Queries_Paginates_AndExportsCsv()
    {
        var auditApi = Substitute.For<IAuditApi>();
        var filePicker = Substitute.For<IFilePicker>();
        var notifications = Substitute.For<INotificationService>();

        var log1 = new AuditLogDto
        {
            Id = 1,
            UserName = "admin",
            DeviceName = "Terminal 1",
            Action = "RolePermissions.Updated",
            EntityType = "Role",
            EntityId = "2",
            TimestampUtc = DateTime.UtcNow,
        };

        var pagedResult = new PagedResult<AuditLogDto>
        {
            Items = new List<AuditLogDto> { log1 },
            Page = 1,
            PageSize = 50,
            TotalCount = 100,
        };

        auditApi.QueryAsync(Arg.Any<AuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<PagedResult<AuditLogDto>>.Ok(pagedResult));

        var csvBytes = new byte[] { 0xEF, 0xBB, 0xBF, 0x61, 0x62, 0x63 }; // BOM + abc
        auditApi.ExportCsvAsync(Arg.Any<AuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<byte[]>.Ok(csvBytes));

        var tempCsvFile = Path.Combine(Path.GetTempPath(), $"audit_test_{Guid.NewGuid()}.csv");
        filePicker.PickSavePath(Arg.Any<string>(), Arg.Any<string>())
            .Returns(tempCsvFile);

        var vm = new AuditLogsViewModel(auditApi, filePicker, notifications);
        await vm.OnNavigatedToAsync(null);

        vm.AuditEntries.Should().HaveCount(1);
        vm.TotalCount.Should().Be(100);
        vm.TotalPages.Should().Be(2);

        // Test pagination commands
        vm.PreviousPageCommand.CanExecute(null).Should().BeFalse();
        vm.NextPageCommand.CanExecute(null).Should().BeTrue();

        // Test export CSV
        try
        {
            await vm.ExportCsvCommand.ExecuteAsync(null);
            File.Exists(tempCsvFile).Should().BeTrue();
            var exportedBytes = await File.ReadAllBytesAsync(tempCsvFile);
            exportedBytes.Should().Equal(csvBytes);
        }
        finally
        {
            if (File.Exists(tempCsvFile))
            {
                File.Delete(tempCsvFile);
            }
        }
    }

    [Fact]
    public async Task UsersViewModel_Unlock_And_LogoutAll_CallsApi()
    {
        var usersApi = Substitute.For<IUsersApi>();
        var dialogs = Substitute.For<IDialogService>();
        var notifications = Substitute.For<INotificationService>();

        var lockedUser = new UserDto
        {
            Id = 5,
            Username = "locked_waiter",
            DisplayName = "Locked Waiter",
            IsActive = true,
            IsLockedOut = true,
            FailedLoginCount = 5,
            LockedUntilUtc = DateTime.UtcNow.AddMinutes(30),
            Roles = new[] { "Waiter" },
        };

        usersApi.GetRolesAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<RoleDto>>.Ok(new List<RoleDto>()));
        usersApi.ListAsync(Arg.Any<UserQuery>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<PagedResult<UserDto>>.Ok(new PagedResult<UserDto>
            {
                Items = new List<UserDto> { lockedUser },
                TotalCount = 1,
                Page = 1,
                PageSize = 50,
            }));

        usersApi.UnlockAsync(5, Arg.Any<CancellationToken>())
            .Returns(ApiResult<UserDto>.Ok(lockedUser with { IsLockedOut = false, FailedLoginCount = 0, LockedUntilUtc = null }));

        usersApi.LogoutAllAsync(5, Arg.Any<CancellationToken>())
            .Returns(ApiResult<object>.Ok(new object()));

        dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(true);

        var vm = new UsersViewModel(usersApi, dialogs, notifications);
        await vm.OnNavigatedToAsync(null);

        vm.Users.Should().HaveCount(1);
        vm.SelectedUser = vm.Users[0];

        // Unlock
        await vm.UnlockCommand.ExecuteAsync(null);
        await usersApi.Received(1).UnlockAsync(5);

        // Logout All Sessions
        await vm.LogoutAllCommand.ExecuteAsync(null);
        await usersApi.Received(1).LogoutAllAsync(5);
    }

    [Fact]
    public void AuditLogsViewModel_UsesLocalDays_AndExportsWithTheScreensFilters()
    {
        var auditApi = Substitute.For<IAuditApi>();
        auditApi.QueryAsync(Arg.Any<AuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<PagedResult<AuditLogDto>>.Ok(new PagedResult<AuditLogDto>()));
        var vm = new AuditLogsViewModel(auditApi, Substitute.For<IFilePicker>(), Substitute.For<INotificationService>())
        {
            UserFilter = " waiter1 ",
            DeviceFilter = "BILLING-01",
            ActionFilter = "Bill.Voided",
        };

        var (todayFrom, todayTo) = vm.GetDateFilter();
        todayFrom.Should().Be(DateTime.Today.ToUniversalTime());
        todayTo.Should().Be(DateTime.Today.AddDays(1).ToUniversalTime().AddTicks(-1));

        vm.DateRange = AuditLogsViewModel.CustomRange;
        vm.CustomFrom = new DateTime(2026, 10, 1);
        vm.CustomTo = new DateTime(2026, 10, 3);
        var (customFrom, customTo) = vm.GetDateFilter();
        customFrom.Should().Be(new DateTime(2026, 10, 1).ToUniversalTime());
        customTo.Should().Be(new DateTime(2026, 10, 4).ToUniversalTime().AddTicks(-1), "the last day is included in full");

        var exported = AuditApi.QueryString(vm.BuildQuery(), includePaging: false);
        exported.Should().Contain("userName=waiter1").And.Contain("deviceName=BILLING-01").And.Contain("action=Bill.Voided")
            .And.Contain("from=").And.Contain("to=").And.NotContain("page=");
    }
}
