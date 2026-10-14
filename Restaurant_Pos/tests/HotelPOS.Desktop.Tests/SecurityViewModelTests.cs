using FluentAssertions;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Devices;
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

        devicesApi.DeactivateAsync(deviceId, Arg.Any<CancellationToken>())
            .Returns(ApiResult<DeviceDto>.Ok(device with { IsActive = false, DeactivatedAtUtc = DateTime.UtcNow }));

        dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(true);

        var vm = new DevicesViewModel(devicesApi, dialogs, notifications, realtime, dispatcher);
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
}
