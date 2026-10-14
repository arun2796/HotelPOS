using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

// Phase 10 load test: N virtual waiters run the whole service cycle (order -> kitchen -> serve -> bill -> payment)
// against a running API for a fixed time, while a cashier hub connection counts BillRequested events.
// Run it against a rehearsal database with the demo users (Seed:DemoData), never against a live venue:
//   dotnet run --project tools/HotelPOS.LoadTest -- --api http://192.168.1.100:5000 --clients 20 --minutes 10
// Exit code 0 when every step's p95 is within --p95 (ms) and no request failed unexpectedly.

var options = LoadOptions.Parse(args);
Console.WriteLine($"HotelPOS load test: {options.Clients} clients for {options.Duration.TotalMinutes:N1} min against {options.Api}");

var stats = new Stats();
using var http = new HttpClient { BaseAddress = options.Api, Timeout = TimeSpan.FromSeconds(30) };
var waiter = await LoginAsync(http, options.Waiter, options.Password);
var kitchen = await LoginAsync(http, options.Kitchen, options.Password);
var cashier = await LoginAsync(http, options.Cashier, options.Password);
var menu = (await GetAsync<MenuDto>(http, waiter, "api/menu", stats, "menu"))!;
var orderable = menu.Items.Where(i => i.IsAvailable).ToList();

var billRequestedEvents = 0;
await using var hub = new HubConnectionBuilder()
    .WithUrl(new Uri(options.Api, "hubs/restaurant"), o => o.AccessTokenProvider = () => Task.FromResult<string?>(cashier))
    .AddJsonProtocol(o => PosJson.Apply(o.PayloadSerializerOptions))
    .Build();
hub.On<BillRequestedEvent>(HubEvents.BillRequested, _ => Interlocked.Increment(ref billRequestedEvents));
await hub.StartAsync();

var tableLocks = new ConcurrentDictionary<int, bool>();
var billsRequested = 0;
var cycles = 0;
var stopAt = DateTime.UtcNow + options.Duration;
var workers = Enumerable.Range(0, options.Clients).Select(async worker =>
{
    var random = new Random(worker);
    while (DateTime.UtcNow < stopAt)
    {
        try
        {
            if (await RunCycleAsync(random))
            {
                Interlocked.Increment(ref cycles);
            }
        }
        catch (Exception ex)
        {
            stats.Error("cycle", ex.Message);
        }
    }
});
await Task.WhenAll(workers);
await Task.Delay(2000); // late hub events
Console.WriteLine(stats.Report(cycles, billsRequested, billRequestedEvents, options.P95Ms, out var passed));
return passed && billRequestedEvents >= billsRequested ? 0 : 1;

async Task<bool> RunCycleAsync(Random random)
{
    var map = (await GetAsync<TableMapDto>(http, waiter, "api/tables", stats, "table map"))!;
    var free = map.Sections.SelectMany(s => s.Tables).Where(t => t.Status == TableStatus.Available && tableLocks.TryAdd(t.Id, true)).FirstOrDefault();
    if (free is null)
    {
        await Task.Delay(200); // every table busy: like a full restaurant, wait for one to free up
        return false;
    }

    try
    {
        var items = Enumerable.Range(0, random.Next(1, 4)).Select(_ =>
        {
            var entry = orderable[random.Next(orderable.Count)];
            var required = menu.ModifierGroups.Where(g => entry.ModifierGroupIds.Contains(g.Id) && g.MinSelections > 0).Select(g => g.Options.First().Id);
            return new OrderItemInput { MenuItemId = entry.Id, Quantity = random.Next(1, 3), ModifierOptionIds = required.ToList() };
        }).ToList();

        var order = await SendAsync<OrderDetailDto>(http, waiter, HttpMethod.Post, "api/orders",
            new CreateOrderRequest { TableId = free.Id, GuestCount = random.Next(1, 5), Items = items, Submit = true }, stats, "submit order", idempotent: true);
        if (order is null)
        {
            return false;
        }

        foreach (var ticket in order.Tickets)
        {
            await SendAsync<object>(http, kitchen, HttpMethod.Post, $"api/kitchen/orders/{ticket.Id}/start", null, stats, "kitchen start");
            await SendAsync<object>(http, kitchen, HttpMethod.Post, $"api/kitchen/orders/{ticket.Id}/ready", null, stats, "kitchen ready");
        }

        await SendAsync<OrderDetailDto>(http, waiter, HttpMethod.Post, $"api/orders/{order.Id}/serve", null, stats, "serve");
        var bill = await SendAsync<BillDetailDto>(http, waiter, HttpMethod.Post, $"api/orders/{order.Id}/request-bill", null, stats, "request bill", idempotent: true);
        if (bill is null)
        {
            return false;
        }

        Interlocked.Increment(ref billsRequested);
        var finalized = await SendAsync<BillDetailDto>(http, cashier, HttpMethod.Post, $"api/billing/{bill.Id}/finalize",
            new BillActionRequest { RowVersion = bill.RowVersion }, stats, "finalize");
        if (finalized is null)
        {
            return false;
        }

        var paid = await SendAsync<BillDetailDto>(http, cashier, HttpMethod.Post, $"api/billing/{bill.Id}/payments",
            new AddPaymentRequest { PaymentMethodId = 1, Amount = finalized.GrandTotal, RowVersion = finalized.RowVersion }, stats, "payment", idempotent: true);
        return paid is not null;
    }
    finally
    {
        tableLocks.TryRemove(free.Id, out _);
    }
}

static async Task<string> LoginAsync(HttpClient http, string username, string password)
{
    var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequest { Username = username, Password = password }, PosJson.Options);
    var body = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(PosJson.Options);
    return body?.Data?.AccessToken ?? throw new InvalidOperationException($"Login of {username} failed: {body?.Message}");
}

static Task<T?> GetAsync<T>(HttpClient http, string token, string url, Stats stats, string step) =>
    SendAsync<T>(http, token, HttpMethod.Get, url, null, stats, step);

static async Task<T?> SendAsync<T>(HttpClient http, string token, HttpMethod method, string url, object? body, Stats stats, string step, bool idempotent = false)
{
    using var request = new HttpRequestMessage(method, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    if (idempotent)
    {
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
    }

    if (body is not null)
    {
        request.Content = JsonContent.Create(body, options: PosJson.Options);
    }

    var watch = Stopwatch.StartNew();
    using var response = await http.SendAsync(request);
    var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(PosJson.Options);
    watch.Stop();
    stats.Record(step, watch.Elapsed.TotalMilliseconds);

    if (response.IsSuccessStatusCode && envelope?.Success == true)
    {
        return envelope.Data;
    }

    // Two waiters reaching for the same table is normal in a busy restaurant, not a failure.
    var code = envelope?.Errors.FirstOrDefault()?.Code;
    if (response.StatusCode == HttpStatusCode.Conflict && code is ErrorCodes.TableNotAvailable or ErrorCodes.ConcurrencyConflict)
    {
        stats.Conflict(step);
    }
    else
    {
        stats.Error(step, $"{(int)response.StatusCode} {code}: {envelope?.Message}");
    }

    return default;
}

internal sealed record LoadOptions(Uri Api, int Clients, TimeSpan Duration, double P95Ms, string Waiter, string Kitchen, string Cashier, string Password)
{
    public static LoadOptions Parse(string[] args)
    {
        string Value(string name, string fallback)
        {
            var index = Array.IndexOf(args, "--" + name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        var api = Value("api", "http://localhost:5000");
        return new LoadOptions(
            new Uri(api.EndsWith('/') ? api : api + "/"),
            int.Parse(Value("clients", "20"), CultureInfo.InvariantCulture),
            TimeSpan.FromMinutes(double.Parse(Value("minutes", "10"), CultureInfo.InvariantCulture)),
            double.Parse(Value("p95", "300"), CultureInfo.InvariantCulture),
            Value("waiter", "waiter1"),
            Value("kitchen", "kitchen1"),
            Value("cashier", "cashier1"),
            Value("password", "Pass@123"));
    }
}

internal sealed class Stats
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<double>> _timings = new();
    private readonly ConcurrentDictionary<string, int> _conflicts = new();
    private readonly ConcurrentDictionary<string, int> _errors = new();
    private readonly ConcurrentQueue<string> _firstErrors = new();

    public void Record(string step, double milliseconds) => _timings.GetOrAdd(step, _ => new()).Add(milliseconds);

    public void Conflict(string step) => _conflicts.AddOrUpdate(step, 1, (_, n) => n + 1);

    public void Error(string step, string message)
    {
        _errors.AddOrUpdate(step, 1, (_, n) => n + 1);
        if (_firstErrors.Count < 10)
        {
            _firstErrors.Enqueue($"{step}: {message}");
        }
    }

    public string Report(int cycles, int billsRequested, int billEvents, double p95Budget, out bool passed)
    {
        var lines = new List<string> { string.Empty, $"{"step",-14} {"calls",7} {"p50 ms",8} {"p95 ms",8} {"max ms",8} {"conflicts",9} {"errors",7}" };
        passed = _errors.IsEmpty;
        foreach (var (step, bag) in _timings.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var sorted = bag.Order().ToArray();
            double Percentile(double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
            var p95 = Percentile(0.95);
            passed &= p95 <= p95Budget;
            lines.Add($"{step,-14} {sorted.Length,7} {Percentile(0.5),8:N0} {p95,8:N0} {sorted[^1],8:N0} {_conflicts.GetValueOrDefault(step),9} {_errors.GetValueOrDefault(step),7}"
                + (p95 > p95Budget ? "  <-- over budget" : string.Empty));
        }

        lines.Add(string.Empty);
        lines.Add($"Completed cycles (order to payment): {cycles}");
        lines.Add($"BillRequested events received by the cashier hub: {billEvents} of {billsRequested}");
        lines.AddRange(_firstErrors.Select(e => "ERROR " + e));
        passed &= billEvents >= billsRequested;
        lines.Add(passed ? $"PASS: every p95 within {p95Budget} ms, no errors, every event delivered." : "FAIL");
        return string.Join(Environment.NewLine, lines);
    }
}
