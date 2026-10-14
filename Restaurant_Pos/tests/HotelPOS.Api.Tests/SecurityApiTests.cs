using System.Globalization;
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
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Realtime;
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
    public async Task DeactivatingDevice_ClosesItsOpenHubConnection_AfterServerNotice_AndAnnouncesPresence()
    {
        var deviceName = "POS-TERMINAL-LIVE-" + Guid.NewGuid().ToString("N")[..6];
        var login = await _factory.LoginAsync("waiter1", ApiFactory.DemoPassword, deviceName: deviceName);
        var deviceId = login.DeviceId!.Value;
        var adminLogin = await _factory.LoginAsync("admin", ApiFactory.AdminPassword);

        await using var adminHub = _factory.CreateHubConnection(adminLogin.AccessToken);
        var presence = new List<DeviceStatusChangedEvent>();
        adminHub.On<DeviceStatusChangedEvent>(HubEvents.DeviceStatusChanged, e =>
        {
            lock (presence)
            {
                presence.Add(e);
            }
        });
        await adminHub.StartAsync();

        await using var deviceHub = _factory.CreateHubConnection(login.AccessToken);
        var notice = new TaskCompletionSource<ServerNoticeEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        deviceHub.On<ServerNoticeEvent>(HubEvents.ServerNotice, e => notice.TrySetResult(e));
        deviceHub.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        await deviceHub.StartAsync();

        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);
        (await admin.PostAsync($"/api/admin/devices/{deviceId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await notice.Task.WaitAsync(TimeSpan.FromSeconds(10))).Code.Should().Be(ErrorCodes.DeviceDisabled);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() =>
        {
            lock (presence)
            {
                return presence.Any(e => e.DeviceId == deviceId && !e.IsOnline);
            }
        });

        lock (presence)
        {
            presence.Should().Contain(e => e.DeviceId == deviceId && e.IsOnline && e.IsActive);
            presence.Where(e => e.DeviceId == deviceId && !e.IsOnline).Should().OnlyContain(e => !e.IsActive);
        }

        await admin.PostAsync($"/api/admin/devices/{deviceId}/activate", null);
    }

    [Fact]
    public async Task DeactivatedUser_IsRefusedAtOnce_EvenWithAnUnexpiredAccessToken()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);
        var username = "leaver_" + Guid.NewGuid().ToString("N")[..8];
        var created = (await (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = username,
            DisplayName = "Leaver",
            Password = "Password123",
            Roles = new[] { Roles.Waiter },
            MustChangePassword = false,
        }, PosJson.Options)).ReadEnvelopeAsync<UserDto>()).Data!;
        var login = await _factory.LoginAsync(username, "Password123");
        using var leaver = _factory.CreateClient();
        leaver.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        (await leaver.GetAsync("/api/tables")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsync($"/api/users/{created.Id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var refused = await leaver.GetAsync("/api/tables");
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.ReadEnvelopeAsync<object>()).Errors.Single().Code.Should().Be(ErrorCodes.AccountDisabled);
        await using var hub = _factory.CreateHubConnection(login.AccessToken);
        var connect = () => hub.StartAsync();
        await connect.Should().ThrowAsync<Exception>("a deactivated user's token must not open a hub connection");

        (await admin.PostAsync($"/api/users/{created.Id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await leaver.GetAsync("/api/tables")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the expected real-time event should arrive");
            await Task.Delay(50);
        }
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

    // Phase 9 § 10: the end-to-end scenario (order -> kitchen -> bill -> discount -> payment -> reprint -> refund) leaves
    // exactly the expected trail on its own order, tickets and bill: every expected action once, nothing missing.
    [Fact]
    public async Task AuditCompleteness_EndToEndScenario_LeavesTheExpectedTrailExactlyOnce()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);
        var cashier = await _factory.CreateAuthenticatedClientAsync("cashier1", ApiFactory.DemoPassword);
        var startUtc = DateTime.UtcNow.AddSeconds(-5);

        var order = await ServedOrderAsync("T07", "Chicken Biryani");
        var bill = await RequestBillAsync(order.Id);

        var discounted = await SendAsync<BillDetailDto>(cashier, HttpMethod.Put, $"/api/billing/{bill.Id}/discount", new ApplyDiscountRequest
        {
            Type = DiscountType.Percentage,
            Value = 5,
            Reason = "Regular guest",
            RowVersion = bill.RowVersion,
        });
        var finalized = await SendAsync<BillDetailDto>(cashier, HttpMethod.Post, $"/api/billing/{bill.Id}/finalize",
            new BillActionRequest { RowVersion = discounted.RowVersion });
        var paid = await SendAsync<BillDetailDto>(cashier, HttpMethod.Post, $"/api/billing/{bill.Id}/payments", new AddPaymentRequest
        {
            PaymentMethodId = 1,
            Amount = finalized.GrandTotal,
            RowVersion = finalized.RowVersion,
        }, idempotent: true);
        await SendAsync<object>(cashier, HttpMethod.Post, "/api/print/reprints", new ReprintRequest
        {
            DocumentType = PrintDocumentType.Invoice,
            EntityId = bill.Id,
            Reason = "Guest asked for a copy",
        });

        using var anonymous = _factory.CreateClient();
        var token = await SendAsync<ApprovalTokenResponse>(anonymous, HttpMethod.Post, "/api/auth/approval", new ApprovalTokenRequest
        {
            ApproverUsername = "manager1",
            ApproverPassword = ApiFactory.DemoPassword,
            ActionType = ApprovalActions.Refund,
        });
        await SendAsync<BillDetailDto>(cashier, HttpMethod.Post, $"/api/billing/{bill.Id}/refunds", new RefundRequest
        {
            PaymentId = paid.Payments.First().Id,
            Amount = paid.GrandTotal,
            Reason = "Customer complaint",
            Approval = new ManagerApprovalDto { ApprovalToken = token.Token },
            RowVersion = paid.RowVersion,
        }, idempotent: true);

        var trail = new List<AuditLogDto>();
        for (var page = 1; ; page++)
        {
            var result = (await (await admin.GetAsync($"/api/admin/audit-logs?page={page}&pageSize=200&from={Uri.EscapeDataString(startUtc.ToString("O"))}"))
                .ReadEnvelopeAsync<PagedResult<AuditLogDto>>()).Data!;
            trail.AddRange(result.Items);
            if (page >= result.TotalPages)
            {
                break;
            }
        }

        var ticketIds = order.Tickets.Select(t => t.Id.ToString(CultureInfo.InvariantCulture)).ToHashSet();
        var actions = trail
            .Where(a => (a.EntityType == "Order" && a.EntityId == order.Id.ToString(CultureInfo.InvariantCulture))
                || (a.EntityType == "KitchenTicket" && ticketIds.Contains(a.EntityId!))
                || (a.EntityType == "Bill" && a.EntityId == bill.Id.ToString(CultureInfo.InvariantCulture)))
            .Select(a => a.Action)
            .ToList();
        var perTicket = new[] { AuditActions.KitchenTicketStarted, AuditActions.KitchenTicketReady };

        actions.Should().BeEquivalentTo(
            new[]
            {
                AuditActions.OrderSubmitted,
                AuditActions.OrderServed,
                AuditActions.BillRequested,
                AuditActions.BillDiscountApplied,
                AuditActions.BillFinalized,
                AuditActions.PaymentReceived,
                AuditActions.BillSettled,
                AuditActions.PrintReprint,
                AuditActions.PaymentRefunded,
            }.Concat(order.Tickets.SelectMany(_ => perTicket)),
            "each step of the scenario must be audited exactly once on its own entity");
        trail.Should().ContainSingle(a => a.Action == AuditActions.ApprovalTokenIssued && a.NewValues!.Contains("refund"));
    }

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string url, object body, bool idempotent = false)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: PosJson.Options) };
        if (idempotent)
        {
            request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        }

        var envelope = await (await client.SendAsync(request)).ReadEnvelopeAsync<T>();
        envelope.Success.Should().BeTrue($"{method} {url}: {envelope.Message}");
        return envelope.Data!;
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
