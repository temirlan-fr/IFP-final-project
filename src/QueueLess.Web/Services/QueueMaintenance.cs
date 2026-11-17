using QueueLess.Infrastructure;
namespace QueueLess.Web.Services;
public sealed class QueueMaintenance(IServiceScopeFactory scopes, ILogger<QueueMaintenance> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<QueueService>().MaintainAsync(); }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Queue maintenance will retry on the next tick"); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
