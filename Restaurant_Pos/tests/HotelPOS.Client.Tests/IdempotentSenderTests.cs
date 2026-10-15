using System.Net;
using FluentAssertions;
using HotelPOS.Contracts.Common;
using HotelPOS.Client.Services;
using Xunit;

namespace HotelPOS.Client.Tests;

public class IdempotentSenderTests
{
    private static IdempotentSender NoWait() =>
        new(new[] { TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero }, (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Retries_UnknownOutcomes_WithTheSameKey_AndStopsOnSuccess()
    {
        var keys = new List<Guid>();
        var attempt = 0;

        var result = await NoWait().SendAsync(IdempotentSender.NewKey(), (key, _) =>
        {
            keys.Add(key);
            attempt++;
            return Task.FromResult(attempt < 3
                ? ApiResult<string>.Fail(ErrorCodes.ConnectionUnavailable, "offline")
                : ApiResult<string>.Ok("done", HttpStatusCode.OK, replayed: attempt > 1));
        });

        result.Success.Should().BeTrue();
        keys.Should().HaveCount(3).And.OnlyContain(k => k == keys[0], "the key must not change between retries");
    }

    [Fact]
    public async Task DoesNotRetry_DefiniteAnswers()
    {
        var attempts = 0;

        var result = await NoWait().SendAsync(IdempotentSender.NewKey(), (_, _) =>
        {
            attempts++;
            return Task.FromResult(ApiResult<string>.Fail(ErrorCodes.ValidationError, "bad input", HttpStatusCode.BadRequest));
        });

        result.Success.Should().BeFalse();
        attempts.Should().Be(1, "a 4xx is final; retrying would not change it");
    }

    [Fact]
    public async Task GivesUp_AfterTheLastDelay_AndReturnsTheLastOutcome()
    {
        var attempts = 0;

        var result = await NoWait().SendAsync(IdempotentSender.NewKey(), (_, _) =>
        {
            attempts++;
            return Task.FromResult(ApiResult<string>.Fail(ErrorCodes.ServerError, "boom", HttpStatusCode.InternalServerError));
        });

        result.IsTransient.Should().BeTrue();
        attempts.Should().Be(4, "one first attempt plus three retries");
    }
}
