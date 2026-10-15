using HotelPOS.Contracts.Common;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HotelPOS.Desktop.Tests;

public class ViewModelBaseTests
{
    private readonly TestViewModel _vm = new();

    [Fact]
    public async Task ExecuteAsync_WhileTheOperationRuns_IsBusy_AndAfterwardsIsNot()
    {
        var busyInside = false;

        await _vm.RunAsync(() =>
        {
            busyInside = _vm.IsBusy;
            return Task.CompletedTask;
        });

        busyInside.Should().BeTrue();
        _vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenItStarts_ClearsThePreviousError()
    {
        _vm.ErrorMessage = "Earlier failure";
        string? errorInside = "not read";

        await _vm.RunAsync(() =>
        {
            errorInside = _vm.ErrorMessage;
            return Task.CompletedTask;
        });

        errorInside.Should().BeNull();
        _vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheOperationThrows_ShowsAPlainMessage_ResetsBusy_AndLogsTheException()
    {
        var sink = new CapturingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var failure = new InvalidOperationException("boom");
        try
        {
            await _vm.RunAsync(() => throw failure);
        }
        finally
        {
            Log.Logger = previous;
        }

        _vm.ErrorMessage.Should().Be(ViewModelBase.UnexpectedErrorMessage);
        _vm.IsBusy.Should().BeFalse();
        sink.Events.Should().Contain(e => e.Level == LogEventLevel.Error && e.Exception == failure);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_LetsTheCancellationThrough_AndResetsBusy()
    {
        var act = () => _vm.RunAsync(() => throw new OperationCanceledException());

        await act.Should().ThrowAsync<OperationCanceledException>();
        _vm.IsBusy.Should().BeFalse();
        _vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task OnBusyChanged_IsCalledWhenTheWorkStartsAndWhenItEnds()
    {
        await _vm.RunAsync(() => Task.CompletedTask);

        _vm.BusyChanges.Should().Equal(true, false);
    }

    [Fact]
    public async Task FetchAsync_OnSuccess_ReturnsTheData_WithoutAnError()
    {
        var data = await _vm.FetchAsync(Task.FromResult(ApiResult<List<int>>.Ok(new List<int> { 1, 2 })));

        data.Should().Equal(1, 2);
        _vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_OnAFailure_ReturnsNull_AndShowsTheServerMessage()
    {
        var data = await _vm.FetchAsync(Task.FromResult(ApiResult<List<int>>.Fail(ErrorCodes.NotFound, "The section was not found.")));

        data.Should().BeNull();
        _vm.ErrorMessage.Should().Be("The section was not found.");
    }

    [Fact]
    public async Task FetchAsync_OnAValidationFailure_ShowsTheFieldMessages()
    {
        var invalid = new ApiResult<List<int>>
        {
            Message = "One or more fields are invalid.",
            Errors = new[] { new ApiError(ErrorCodes.ValidationError, "The end date is before the start date.", "To") },
        };

        await _vm.FetchAsync(Task.FromResult(invalid));

        _vm.ErrorMessage.Should().Be("The end date is before the start date.");
    }

    [Fact]
    public async Task FetchAsync_OnAConnectionFailure_SaysTheConnectionIsUnavailable_NotThatAChangeWasNotSaved()
    {
        var data = await _vm.FetchAsync(Task.FromResult(ApiResult<List<int>>.ConnectionFailure(ApiClient.ConnectionUnavailableMessage)));

        data.Should().BeNull();
        _vm.ErrorMessage.Should().StartWith("Connection unavailable.").And.NotContain("NOT saved");
    }

    [Fact]
    public async Task FetchAsync_WhenTheServerSendsNoData_ReturnsNull()
    {
        var data = await _vm.FetchAsync(Task.FromResult(ApiResult<List<int>>.Ok(null)));

        data.Should().BeNull();
    }

    private sealed class TestViewModel : ViewModelBase
    {
        public List<bool> BusyChanges { get; } = new();

        public Task RunAsync(Func<Task> operation) => ExecuteAsync(operation);

        public new Task<T?> FetchAsync<T>(Task<ApiResult<T>> call)
            where T : class => base.FetchAsync(call);

        protected override void OnBusyChanged() => BusyChanges.Add(IsBusy);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent)
        {
            lock (Events)
            {
                Events.Add(logEvent);
            }
        }
    }
}
