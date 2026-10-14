using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HotelPOS.Api.Common;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Application.Common;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace HotelPOS.Api.Tests;

public class SecurityApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SecurityApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Lockout_After5Failures_Returns403_AndAdminCanUnlock()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);
        var username = "lockout_user_" + Guid.NewGuid().ToString("N")[..8];

        var createRes = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = username,
            DisplayName = "Lockout Test User",
            Password = "Password123",
            Roles = new[] { "Waiter" },
            MustChangePassword = false,
        }, PosJson.Options);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdUser = (await createRes.ReadEnvelopeAsync<UserDto>()).Data!;

        using var client = _factory.CreateClient();

        // 4 failed attempts should return 401
        for (var i = 1; i <= 4; i++)
        {
            var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Username = username,
                Password = "WrongPassword123",
            }, PosJson.Options);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var body = await res.ReadEnvelopeAsync<object>();
            body.Errors.Single().Code.Should().Be(ErrorCodes.InvalidCredentials);
        }

        // 5th failed attempt triggers lockout and returns 403 AccountLocked
        var fifthRes = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = username,
            Password = "WrongPassword123",
        }, PosJson.Options);
        fifthRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var fifthBody = await fifthRes.ReadEnvelopeAsync<object>();
        fifthBody.Errors.Single().Code.Should().Be(ErrorCodes.AccountLocked);

        // 6th attempt even with correct password returns 403 AccountLocked
        var sixthRes = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = username,
            Password = "Password123",
        }, PosJson.Options);
        sixthRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var sixthBody = await sixthRes.ReadEnvelopeAsync<object>();
        sixthBody.Errors.Single().Code.Should().Be(ErrorCodes.AccountLocked);

        // Verify user DTO reflects lockout
        var getUserRes = await admin.GetAsync($"/api/users/{createdUser.Id}");
        var userDto = (await getUserRes.ReadEnvelopeAsync<UserDto>()).Data!;
        userDto.IsLockedOut.Should().BeTrue();
        userDto.FailedLoginCount.Should().Be(5);
        userDto.LockedUntilUtc.Should().NotBeNull();

        // Admin unlocks user
        var unlockRes = await admin.PostAsync($"/api/users/{createdUser.Id}/unlock", null);
        unlockRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Now user can log in with correct password
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = username,
            Password = "Password123",
        }, PosJson.Options);
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginRes.ReadEnvelopeAsync<LoginResponse>();
        loginBody.Success.Should().BeTrue();
        loginBody.Data!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RateLimiter_Returns429_AndRetryAfter_WhenExceeded()
    {
        using var factory = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:RateLimit:MaxPerUser"] = "3",
                ["Security:RateLimit:MaxPerIp"] = "100",
            }));
        });

        using var client = factory.CreateClient();
        var username = "ratelimit_user_" + Guid.NewGuid().ToString("N")[..8];

        for (var i = 1; i <= 3; i++)
        {
            var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Username = username,
                Password = "AnyPassword123",
            }, PosJson.Options);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // 4th attempt exceeds MaxPerUser (3) -> returns 429
        var limitedRes = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = username,
            Password = "AnyPassword123",
        }, PosJson.Options);

        limitedRes.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limitedRes.Headers.Contains("Retry-After").Should().BeTrue();
        var body = await limitedRes.ReadEnvelopeAsync<object>();
        body.Errors.Single().Code.Should().Be(ErrorCodes.RateLimited);
    }

    [Fact]
    public async Task RateLimiter_CoversTrailingSlashAndApprovalEndpoint()
    {
        using var factory = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:RateLimit:MaxPerUser"] = "2",
                ["Security:RateLimit:MaxPerIp"] = "100",
            }));
        });
        using var client = factory.CreateClient();
        var username = "ratelimit_slash_" + Guid.NewGuid().ToString("N")[..8];

        var statuses = new List<HttpStatusCode>();
        foreach (var path in new[] { "/api/auth/login", "/api/auth/login/", "/API/AUTH/LOGIN/" })
        {
            var res = await client.PostAsJsonAsync(path, new LoginRequest { Username = username, Password = "AnyPassword123" }, PosJson.Options);
            statuses.Add(res.StatusCode);
        }

        var approver = "ratelimit_approver_" + Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 2; i++)
        {
            await client.PostAsJsonAsync("/api/auth/approval", new ApprovalTokenRequest
            {
                ApproverUsername = approver,
                ApproverPassword = "AnyPassword123",
                ActionType = ApprovalActions.Void,
            }, PosJson.Options);
        }

        var approvalLimited = await client.PostAsJsonAsync("/api/auth/approval", new ApprovalTokenRequest
        {
            ApproverUsername = approver,
            ApproverPassword = "AnyPassword123",
            ActionType = ApprovalActions.Void,
        }, PosJson.Options);

        statuses.Should().Equal(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests);
        approvalLimited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ApprovalToken_WithMissingOrUnknownActionType_Returns400()
    {
        using var client = _factory.CreateClient();

        var missing = await client.PostAsync("/api/auth/approval", new StringContent(
            """{"approverUsername":"manager1","approverPassword":"x","actionType":null}""", Encoding.UTF8, "application/json"));
        var unknown = await client.PostAsJsonAsync("/api/auth/approval", new ApprovalTokenRequest
        {
            ApproverUsername = "manager1",
            ApproverPassword = "x",
            ActionType = "launch-missiles",
        }, PosJson.Options);

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ApprovalToken_Workflow_Issue_SingleUse_AndRoleEnforced()
    {
        using var client = _factory.CreateClient();

        // 1. Waiter credentials cannot request approval token
        var waiterReq = await client.PostAsJsonAsync("/api/auth/approval", new ApprovalTokenRequest
        {
            ApproverUsername = "waiter1",
            ApproverPassword = ApiFactory.DemoPassword,
            ActionType = "Discount",
        }, PosJson.Options);
        waiterReq.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var waiterErr = await waiterReq.ReadEnvelopeAsync<object>();
        waiterErr.Errors.Single().Code.Should().Be(ErrorCodes.ApprovalInvalid);

        // 2. Manager credentials can request approval token
        var managerReq = await client.PostAsJsonAsync("/api/auth/approval", new ApprovalTokenRequest
        {
            ApproverUsername = "manager1",
            ApproverPassword = ApiFactory.DemoPassword,
            ActionType = "Discount",
        }, PosJson.Options);
        managerReq.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenBody = await managerReq.ReadEnvelopeAsync<ApprovalTokenResponse>();
        tokenBody.Data!.Token.Should().NotBeNullOrEmpty();
        tokenBody.Data.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);

        var token = tokenBody.Data.Token;

        // 3. Create a bill to apply discount with approval token
        var cashier = await _factory.CreateAuthenticatedClientAsync("cashier1", ApiFactory.DemoPassword);
        var order = await ServedOrderAsync("T06", "Chicken Biryani");
        var bill = await RequestBillAsync(order.Id);

        // First use: Apply discount using approval token -> OK (15% exceeds default 10% cashier threshold, requiring approval)
        var firstDiscountRes = await cashier.PutAsJsonAsync($"/api/billing/{bill.Id}/discount", new ApplyDiscountRequest
        {
            Type = DiscountType.Percentage,
            Value = 15,
            Reason = "Manager Special",
            Approval = new ManagerApprovalDto
            {
                ApprovalToken = token,
            },
            RowVersion = bill.RowVersion,
        }, PosJson.Options);
        firstDiscountRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Fetch refreshed bill row version
        var billDetail = (await (await cashier.GetAsync($"/api/billing/{bill.Id}")).ReadEnvelopeAsync<BillDetailDto>()).Data!;

        // Second use of the same token -> Rejected (single-use token consumed)
        var secondDiscountRes = await cashier.PutAsJsonAsync($"/api/billing/{bill.Id}/discount", new ApplyDiscountRequest
        {
            Type = DiscountType.Percentage,
            Value = 20,
            Reason = "Second Try",
            Approval = new ManagerApprovalDto
            {
                ApprovalToken = token,
            },
            RowVersion = billDetail.RowVersion,
        }, PosJson.Options);
        secondDiscountRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var secondBody = await secondDiscountRes.ReadEnvelopeAsync<object>();
        secondBody.Errors.Single().Code.Should().Be(ErrorCodes.ApprovalInvalid);
    }

    [Fact]
    public async Task DeactivatedDevice_Returns403_AndRejectsHubConnection()
    {
        var deviceName = "POS-TERMINAL-DEACT-" + Guid.NewGuid().ToString("N")[..6];
        var login = await _factory.LoginAsync("waiter1", ApiFactory.DemoPassword, deviceName: deviceName);
        var deviceId = login.DeviceId!.Value;

        var deviceClient = _factory.CreateClient();
        deviceClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        // Normal authenticated call succeeds
        var beforeDeact = await deviceClient.GetAsync("/api/auth/me");
        beforeDeact.StatusCode.Should().Be(HttpStatusCode.OK);

        // Admin deactivates device
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);
        var deactRes = await admin.PostAsync($"/api/admin/devices/{deviceId}/deactivate", null);
        deactRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // REST calls now return 403 DEVICE_DISABLED
        var afterDeact = await deviceClient.GetAsync("/api/auth/me");
        afterDeact.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var afterBody = await afterDeact.ReadEnvelopeAsync<object>();
        afterBody.Errors.Single().Code.Should().Be("DEVICE_DISABLED");

        // Hub connection is rejected
        var hub = _factory.CreateHubConnection(login.AccessToken);
        var connectAct = async () => await hub.StartAsync();
        await connectAct.Should().ThrowAsync<Exception>();

        // Admin re-activates device
        var actRes = await admin.PostAsync($"/api/admin/devices/{deviceId}/activate", null);
        actRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // REST calls work again
        var afterReactivate = await deviceClient.GetAsync("/api/auth/me");
        afterReactivate.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RolePermissions_QueryAndModify_WithForceRelogin_AndProtectAdmin()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);

        // Query all permissions to find role IDs
        var allRes = await admin.GetAsync("/api/roles/permissions");
        allRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var allPerms = (await allRes.ReadEnvelopeAsync<IReadOnlyList<RolePermissionsDto>>()).Data!;

        var waiterRole = allPerms.Single(r => r.RoleName == "Waiter");
        var adminRole = allPerms.Single(r => r.RoleName == "Admin");
        var kitchenRole = allPerms.Single(r => r.RoleName == "Kitchen");

        // Query permissions for Waiter by ID
        var getRes = await admin.GetAsync($"/api/roles/{waiterRole.RoleId}/permissions");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var perms = (await getRes.ReadEnvelopeAsync<RolePermissionsDto>()).Data!;
        perms.RoleName.Should().Be("Waiter");
        perms.Permissions.Should().Contain(Permissions.OrdersCreate);

        // Admin cannot modify Admin role permissions
        var updateAdminRes = await admin.PutAsJsonAsync($"/api/roles/{adminRole.RoleId}/permissions", new UpdateRolePermissionsRequest
        {
            Permissions = new List<string> { Permissions.TablesView },
        }, PosJson.Options);
        updateAdminRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errBody = await updateAdminRes.ReadEnvelopeAsync<object>();
        errBody.Errors.Single().Code.Should().Be(ErrorCodes.BusinessRule);

        // Create a dedicated user to test force-relogin revocation
        var tempUser = "perm_user_" + Guid.NewGuid().ToString("N")[..8];
        await admin.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = tempUser,
            DisplayName = "Perm Test User",
            Password = "Password123",
            Roles = new[] { "Kitchen" },
            MustChangePassword = false,
        }, PosJson.Options);

        var tempLogin = await _factory.LoginAsync(tempUser, "Password123");

        // Update Kitchen permissions with ForceRelogin = true
        var updateKitchenRes = await admin.PutAsJsonAsync($"/api/roles/{kitchenRole.RoleId}/permissions", new UpdateRolePermissionsRequest
        {
            Permissions = new List<string> { Permissions.KitchenOperate },
            ForceRelogin = true,
        }, PosJson.Options);
        updateKitchenRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Refresh token should now be revoked
        using var client = _factory.CreateClient();
        var refreshRes = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = tempLogin.RefreshToken,
        }, PosJson.Options);
        refreshRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuditLogs_Query_AndCsvExport_WithUtf8Bom()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);

        // Make an admin settings change to guarantee an audit entry
        await admin.PutAsJsonAsync("/api/admin/settings", new UpdateSettingsRequest
        {
            Items = new[] { new SettingValue { Key = SettingKeys.Address, Value = "Audit Test Address " + Guid.NewGuid().ToString("N") } },
        }, PosJson.Options);

        // Query audit logs with filter
        var queryRes = await admin.GetAsync("/api/admin/audit-logs?page=1&pageSize=20");
        queryRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var logs = (await queryRes.ReadEnvelopeAsync<PagedResult<AuditLogDto>>()).Data!;
        logs.Items.Should().NotBeEmpty();

        // CSV export
        var exportRes = await admin.GetAsync("/api/admin/audit-logs/export");
        exportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        exportRes.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var bytes = await exportRes.Content.ReadAsByteArrayAsync();
        // Check UTF-8 BOM: 0xEF, 0xBB, 0xBF
        bytes.Length.Should().BeGreaterThan(3);
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);

        var csvString = Encoding.UTF8.GetString(bytes);
        csvString.Should().Contain("Id,TimestampUtc,UserId,UserName,DeviceId,DeviceName,MachineName,IpAddress,Action,EntityType,EntityId,OldValues,NewValues,CorrelationId");
    }

    [Fact]
    public async Task AuditCompleteness_EndToEndScenario_GeneratesExpectedAuditTrail()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);

        // Record timestamp before workflow
        var startTime = DateTime.UtcNow.AddSeconds(-5);

        // 1. Occupy table / Order created & served
        var order = await ServedOrderAsync("T07", "Chicken Biryani");

        // 2. Request bill
        var bill = await RequestBillAsync(order.Id);

        // 3. Finalize bill
        var cashier = await _factory.CreateAuthenticatedClientAsync("cashier1", ApiFactory.DemoPassword);
        var finalizeRes = await cashier.PostAsJsonAsync($"/api/billing/{bill.Id}/finalize", new BillActionRequest
        {
            RowVersion = bill.RowVersion,
        }, PosJson.Options);
        finalizeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalizedBill = (await finalizeRes.ReadEnvelopeAsync<BillDetailDto>()).Data!;

        // 4. Pay bill
        var payReq = new HttpRequestMessage(HttpMethod.Post, $"/api/billing/{bill.Id}/payments")
        {
            Content = JsonContent.Create(new AddPaymentRequest
            {
                PaymentMethodId = 1,
                Amount = finalizedBill.GrandTotal,
                RowVersion = finalizedBill.RowVersion,
            }, options: PosJson.Options),
        };
        payReq.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var payRes = await cashier.SendAsync(payReq);
        payRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var paidBill = (await payRes.ReadEnvelopeAsync<BillDetailDto>()).Data!;

        // 5. Refund bill (requires manager credentials)
        var refundReq = new HttpRequestMessage(HttpMethod.Post, $"/api/billing/{bill.Id}/refunds")
        {
            Content = JsonContent.Create(new RefundRequest
            {
                PaymentId = paidBill.Payments.First().Id,
                Amount = paidBill.GrandTotal,
                Reason = "Customer Complaint",
                Approval = new ManagerApprovalDto
                {
                    ApproverUsername = "manager1",
                    ApproverPassword = ApiFactory.DemoPassword,
                },
                RowVersion = paidBill.RowVersion,
            }, options: PosJson.Options),
        };
        refundReq.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var refundRes = await cashier.SendAsync(refundReq);
        refundRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Query audit logs generated during this run
        var auditRes = await admin.GetAsync($"/api/admin/audit-logs?pageSize=100&fromUtc={startTime:O}");
        auditRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditEntries = (await auditRes.ReadEnvelopeAsync<PagedResult<AuditLogDto>>()).Data!.Items;

        var actions = auditEntries.Select(a => a.Action).ToList();

        // Verify key business events exist in the audit log
        actions.Should().Contain(AuditActions.OrderSubmitted);
        actions.Should().Contain(AuditActions.OrderServed);
        actions.Should().Contain(AuditActions.BillRequested);
        actions.Should().Contain(AuditActions.BillFinalized);
        actions.Should().Contain(AuditActions.PaymentReceived);
        actions.Should().Contain(AuditActions.PaymentRefunded);
    }

    // Helper methods
    private async Task<BillDetailDto> RequestBillAsync(int orderId)
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/request-bill");
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var envelope = await (await waiter.SendAsync(request)).ReadEnvelopeAsync<BillDetailDto>();
        envelope.Success.Should().BeTrue(envelope.Message);
        return envelope.Data!;
    }

    private async Task<OrderDetailDto> ServedOrderAsync(string tableCode, params string[] items)
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var map = (await (await waiter.GetAsync("/api/tables")).ReadEnvelopeAsync<TableMapDto>()).Data!;
        var menu = (await (await waiter.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest
            {
                TableId = map.Sections.SelectMany(s => s.Tables).Single(t => t.Code == tableCode).Id,
                GuestCount = 2,
                Submit = true,
                Items = items.Select(name =>
                {
                    var entry = menu.Items.Single(i => i.Name == name);
                    var required = menu.ModifierGroups.Where(g => entry.ModifierGroupIds.Contains(g.Id) && g.MinSelections > 0)
                        .Select(g => g.Options.First().Id);
                    return new OrderItemInput { MenuItemId = entry.Id, Quantity = 1, ModifierOptionIds = required.ToList() };
                }).ToList(),
            }, options: PosJson.Options),
        };
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var order = (await (await waiter.SendAsync(request)).ReadEnvelopeAsync<OrderDetailDto>()).Data!;

        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        foreach (var ticket in order.Tickets)
        {
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
        }

        var served = await (await waiter.PostAsync($"/api/orders/{order.Id}/serve", null)).ReadEnvelopeAsync<OrderDetailDto>();
        served.Data!.Status.Should().Be(OrderStatus.Served);
        return served.Data;
    }
}
