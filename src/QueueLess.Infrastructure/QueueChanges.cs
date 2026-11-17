using Microsoft.Extensions.Logging;

namespace QueueLess.Infrastructure;

// Subscribers are circuit-scoped components and must unsubscribe on disposal.
public sealed class QueueChanges(ILogger<QueueChanges> logger)
{
    public event Func<Task>? Changed;
    public async Task PublishAsync()
    {
        if (Changed is null) return;
        foreach (var subscriber in Changed.GetInvocationList().Cast<Func<Task>>())
        {
            try { await subscriber(); }
            catch (Exception ex) { logger.LogWarning(ex, "A disconnected queue subscriber could not refresh"); }
        }
    }
}

public sealed class WriteGate
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
}
