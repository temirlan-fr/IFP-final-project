using Microsoft.EntityFrameworkCore;
using QueueLess.Core;
namespace QueueLess.Tests;

public class QueueIntegrationTests : IAsyncLifetime
{
    private readonly QueueTestStore store = new();
    public Task InitializeAsync() => store.InitializeAsync();
    public Task DisposeAsync() => store.DisposeAsync();

    [Fact]
    public async Task FullVisitPreservesFifoAndHistory()
    {
        Assert.True((await store.Service.JoinAsync(store.User(2), 1)).Success);
        Assert.True((await store.Service.JoinAsync(store.User(3), 1)).Success);
        var bob = Assert.Single(await store.Service.GetMyTicketsAsync(store.User(3)));
        Assert.Equal("S-002", bob.Code); Assert.Equal(1, bob.Ahead); Assert.Equal(8, bob.WaitMinutes);
        Assert.True((await store.Service.CallNextAsync(store.User(1), 1)).Success);
        var alice = Assert.Single(await store.Service.GetMyTicketsAsync(store.User(2)));
        Assert.Equal(TicketStatus.Serving, alice.Status);
        Assert.False((await store.Service.CallNextAsync(store.User(1), 1)).Success);
        Assert.True((await store.Service.FinishAsync(store.User(1), alice.Id, TicketStatus.Completed)).Success);
        Assert.True((await store.Service.CallNextAsync(store.User(1), 1)).Success);
        Assert.Equal(TicketStatus.Serving, Assert.Single(await store.Service.GetMyTicketsAsync(store.User(3))).Status);
        var snapshot = await store.Service.GetOperatorSnapshotAsync(store.User(1), 1);
        Assert.Equal(1, snapshot.Completed); Assert.NotEmpty(snapshot.Audit);
    }

    [Fact]
    public async Task DuplicateJoinsAcrossServicesAreRejected()
    {
        var results = await Task.WhenAll(store.Service.JoinAsync(store.User(2), 1), store.Service.JoinAsync(store.User(2), 2));
        Assert.Single(results, r => r.Success);
        Assert.Single(await store.Service.GetMyTicketsAsync(store.User(2)));
    }

    [Fact]
    public async Task ConcurrentVisitorsReceiveUniqueSequentialNumbers()
    {
        await using (var db = store.Factory.CreateDbContext())
        {
            db.Accounts.AddRange(Enumerable.Range(10, 20).Select(id => new Account { Id = id, Name = $"Visitor {id}", Email = $"{id}@test" }));
            await db.SaveChangesAsync();
        }
        var results = await Task.WhenAll(Enumerable.Range(10, 20).Select(id => store.Service.JoinAsync(store.User(id), 1)));
        Assert.All(results, r => Assert.True(r.Success));
        await using var check = store.Factory.CreateDbContext();
        Assert.Equal(Enumerable.Range(1, 20), await check.Tickets.OrderBy(t => t.Sequence).Select(t => t.Sequence).ToListAsync());
    }

    [Fact]
    public async Task TwoOperatorsCannotCallTwoVisitorsAtOnce()
    {
        await store.Service.JoinAsync(store.User(2), 1); await store.Service.JoinAsync(store.User(3), 1);
        var results = await Task.WhenAll(store.Service.CallNextAsync(store.User(1), 1), store.Service.CallNextAsync(store.User(1), 1));
        Assert.Single(results, r => r.Success);
        await using var db = store.Factory.CreateDbContext();
        Assert.Equal(1, await db.Tickets.CountAsync(t => t.Status == TicketStatus.Serving));
    }

    [Fact]
    public async Task OnlyOwnerCanCancelWaitingTicket()
    {
        await store.Service.JoinAsync(store.User(2), 1);
        var ticket = Assert.Single(await store.Service.GetMyTicketsAsync(store.User(2)));
        Assert.False((await store.Service.CancelTicketAsync(store.User(3), ticket.Id)).Success);
        Assert.True((await store.Service.CancelTicketAsync(store.User(2), ticket.Id)).Success);
        Assert.True((await store.Service.JoinAsync(store.User(2), 2)).Success);
    }

    [Fact]
    public async Task ServingTicketCannotBeCancelledOrCompletedTwice()
    {
        await store.Service.JoinAsync(store.User(2), 1); await store.Service.CallNextAsync(store.User(1), 1);
        var ticket = Assert.Single(await store.Service.GetMyTicketsAsync(store.User(2)));
        Assert.False((await store.Service.CancelTicketAsync(store.User(2), ticket.Id)).Success);
        Assert.True((await store.Service.FinishAsync(store.User(1), ticket.Id, TicketStatus.NoShow)).Success);
        Assert.False((await store.Service.FinishAsync(store.User(1), ticket.Id, TicketStatus.Completed)).Success);
    }

    [Fact]
    public async Task DatabaseRoleOverridesForgedRoleClaims()
    {
        Assert.False((await store.Service.CallNextAsync(store.User(2, "Operator"), 1)).Success);
        Assert.False((await store.Service.SetServiceOpenAsync(store.User(2, "Operator"), 1, false)).Success);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.Service.GetOperatorSnapshotAsync(store.User(2, "Operator"), 1));
    }

    [Fact]
    public async Task AnonymousAndExpiredSessionsCannotMutateOrReadPrivateTickets()
    {
        Assert.False((await store.Service.JoinAsync(new(), 1)).Success);
        var expired = store.User(2);
        await store.Service.JoinAsync(expired, 1);
        store.Clock.UtcNow = store.Clock.UtcNow.AddDays(11);
        Assert.False((await store.Service.JoinAsync(expired, 2)).Success);
        Assert.Empty(await store.Service.GetMyTicketsAsync(expired));
    }

    [Fact]
    public async Task PausedServiceRejectsArrivalsButPreservesWaitingTickets()
    {
        await store.Service.JoinAsync(store.User(2), 1);
        Assert.True((await store.Service.SetServiceOpenAsync(store.User(1), 1, false)).Success);
        Assert.False((await store.Service.JoinAsync(store.User(3), 1)).Success);
        Assert.False((await store.Service.CallNextAsync(store.User(1), 1)).Success);
        Assert.Equal(TicketStatus.Waiting, Assert.Single(await store.Service.GetMyTicketsAsync(store.User(2))).Status);
        await store.Service.SetServiceOpenAsync(store.User(1), 1, true);
        Assert.True((await store.Service.CallNextAsync(store.User(1), 1)).Success);
    }

    [Fact]
    public async Task DatabaseConstraintProtectsActiveTicketInvariant()
    {
        await store.Service.JoinAsync(store.User(2), 1);
        await using var db = store.Factory.CreateDbContext();
        db.Tickets.Add(new QueueTicket { AccountId = 2, ServiceId = 2, Day = "2026-11-17", Sequence = 1, Code = "T-001" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task QueueDataSurvivesContextRecreation()
    {
        await store.Service.JoinAsync(store.User(2), 1);
        await using var db = store.Factory.CreateDbContext();
        Assert.Equal("S-001", (await db.Tickets.SingleAsync()).Code);
        Assert.Single(await db.Audit.ToListAsync());
    }

    [Fact]
    public async Task SubscriberFailureDoesNotRollBackCommittedTicket()
    {
        store.Changes.Changed += () => throw new InvalidOperationException("Disconnected circuit");
        Assert.True((await store.Service.JoinAsync(store.User(2), 1)).Success);
        Assert.Single(await store.Service.GetMyTicketsAsync(store.User(2)));
    }

    [Fact]
    public async Task PreviousDayTicketsExpireBeforeNewJoin()
    {
        await store.Service.JoinAsync(store.User(2), 1);
        store.Clock.UtcNow = store.Clock.UtcNow.AddDays(1);
        Assert.True((await store.Service.JoinAsync(store.User(2), 1)).Success);
        var tickets = await store.Service.GetMyTicketsAsync(store.User(2));
        Assert.Contains(tickets, t => t.Status == TicketStatus.NoShow);
        Assert.Single(tickets, t => t.Status == TicketStatus.Waiting);
    }
}
