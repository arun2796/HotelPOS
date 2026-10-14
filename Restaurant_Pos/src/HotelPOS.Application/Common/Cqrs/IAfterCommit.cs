using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Common.Cqrs;

// Side effects of a command that other terminals or processes can observe: real-time events, closing hub
// connections, deleting replaced files. They run once the command's transaction has committed and are dropped
// when it rolls back, so nobody is told about a change that never happened (docs/04 § 4).
public interface IAfterCommit
{
    void Enqueue(Func<Task> action);
}

internal sealed class AfterCommitQueue : IAfterCommit
{
    private readonly List<Func<Task>> _actions = new();
    private readonly ILogger<AfterCommitQueue> _logger;

    public AfterCommitQueue(ILogger<AfterCommitQueue> logger)
    {
        _logger = logger;
    }

    public void Enqueue(Func<Task> action) => _actions.Add(action);

    public void Discard() => _actions.Clear();

    // The data is already committed: a failing step is logged and must not fail the request or skip the others.
    public async Task RunAsync()
    {
        var actions = _actions.ToList();
        _actions.Clear();
        foreach (var action in actions)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A step after the commit failed; the data is saved and clients resync on reconnect");
            }
        }
    }
}
