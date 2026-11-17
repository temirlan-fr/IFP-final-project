using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using QueueLess.Core;
using QueueLess.Infrastructure;
namespace QueueLess.Web.Components.Shared;
public abstract class LivePage : ComponentBase, IDisposable
{
    [Inject] protected QueueService Queues { get; set; } = null!;
    [Inject] protected QueueChanges Changes { get; set; } = null!;
    [Inject] protected AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [Inject] protected ILogger<LivePage> Logger { get; set; } = null!;
    protected ClaimsPrincipal User = new();
    protected bool Busy;
    protected OperationResult? Notice;
    private bool disposed;
    protected override async Task OnInitializedAsync()
    {
        User = (await Authentication.GetAuthenticationStateAsync()).User;
        Changes.Changed += RefreshAsync;
        await LoadAsync();
    }
    protected abstract Task LoadAsync();
    private Task RefreshAsync() => disposed ? Task.CompletedTask : InvokeAsync(async () => { await LoadAsync(); StateHasChanged(); });
    protected async Task RunAsync(Func<Task<OperationResult>> action)
    {
        if (Busy) return;
        Busy = true;
        try { Notice = await action(); await LoadAsync(); }
        catch (Exception ex) { Logger.LogError(ex, "Queue action failed"); Notice = OperationResult.Fail("We couldn't save that change. Please try again."); }
        finally { Busy = false; }
    }
    public void Dispose() { disposed = true; Changes.Changed -= RefreshAsync; GC.SuppressFinalize(this); }
}
