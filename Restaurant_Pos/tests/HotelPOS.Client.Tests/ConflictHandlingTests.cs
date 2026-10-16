using System.Net;
using System.Text;
using FluentAssertions;
using HotelPOS.Client.Services;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Common;
using NSubstitute;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 20.4: HTTP 409 CONCURRENCY_CONFLICT is a typed result the waiter pages turn into "Changed on another device".</summary>
public class ConflictHandlingTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Key)> Calls { get; } = new();

        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues(PosHeaders.IdempotencyKey, out var key);
            Calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath, key?.FirstOrDefault()));
            return Task.FromResult(Respond(request));
        }
    }

    private static WaiterApi ApiFor(StubHandler handler)
    {
        var host = new FakeClientHost();
        var auth = new WebAuthState(Substitute.For<ISessionTransport>(), host);
        return new WaiterApi(new WebApiClient(new HttpClient(handler), host, auth));
    }

    private static HttpResponseMessage Conflict() => new(HttpStatusCode.Conflict)
    {
        Content = new StringContent(
            "{\"success\":false,\"message\":\"The record was changed by someone else.\",\"errors\":[{\"code\":\"CONCURRENCY_CONFLICT\",\"message\":\"stale\"}]}",
            Encoding.UTF8,
            "application/json"),
    };

    [Fact]
    public async Task Occupy_On409ConcurrencyConflict_ReturnsATypedConflict()
    {
        var handler = new StubHandler { Respond = _ => Conflict() };

        var result = await ApiFor(handler).OccupyAsync(5, 2, "AAAA");

        result.Success.Should().BeFalse();
        result.IsConflict.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.Conflict);
        result.IsTransient.Should().BeFalse();
    }

    [Fact]
    public async Task OtherRefusals_AreNotConflicts()
    {
        var handler = new StubHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("{\"success\":false,\"errors\":[{\"code\":\"TABLE_NOT_AVAILABLE\",\"message\":\"taken\"}]}", Encoding.UTF8, "application/json"),
            },
        };

        var result = await ApiFor(handler).OccupyAsync(5, 2, "AAAA");

        result.IsConflict.Should().BeFalse();
    }

    [Fact]
    public async Task Conflict_IsNotRetried_AndTheKeyStaysUsableForTheNextSend()
    {
        var handler = new StubHandler { Respond = _ => Conflict() };
        var api = ApiFor(handler);
        var sender = new IdempotentSender(new[] { TimeSpan.Zero, TimeSpan.Zero }, (_, _) => Task.CompletedTask);
        var key = IdempotentSender.NewKey();

        var result = await sender.SendAsync(key, (k, ct) => api.RequestBillAsync(9, k, ct));

        result.IsConflict.Should().BeTrue();
        handler.Calls.Should().ContainSingle("a definite answer is never retried");
        handler.Calls[0].Key.Should().Be(key.ToString());
        WaiterApi.ConflictMessage.Should().Be("Changed on another device — reloaded");
    }
}
