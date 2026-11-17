using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using QueueLess.Core;
namespace QueueLess.Infrastructure;

public sealed partial class QueueService
{
    public async Task<OperatorSnapshot> GetOperatorSnapshotAsync(ClaimsPrincipal user, int serviceId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var id = UserId(user);
        if (!await db.Accounts.AnyAsync(a => a.Id == id && a.Role == "Operator")) throw new UnauthorizedAccessException();
        var day = QueueRules.Day(Now);
        var query = db.Tickets.AsNoTracking().Where(t => t.ServiceId == serviceId && t.Day == day);
        var active = await query.Where(t => t.Status == TicketStatus.Serving || t.Status == TicketStatus.Waiting).OrderByDescending(t => t.Status).ThenBy(t => t.Sequence)
            .Select(t => new OperatorTicket(t.Id, t.Code, t.Account.Name, t.Status, t.JoinedAt)).Take(100).ToListAsync();
        return new OperatorSnapshot(active, await query.CountAsync(t => t.Status == TicketStatus.Completed),
            await query.CountAsync(t => t.Status == TicketStatus.Cancelled), await query.CountAsync(t => t.Status == TicketStatus.NoShow),
            await db.Audit.AsNoTracking().OrderByDescending(a => a.Id).Take(8).ToListAsync());
    }

    public Task<OperationResult> CallNextAsync(ClaimsPrincipal user, int serviceId) => WriteAsync(user, async (db, id) =>
    {
        var service = await db.Services.FindAsync(serviceId);
        if (service is null || !service.IsOpen) return OperationResult.Fail("Open this service before calling a visitor.");
        if (await db.Tickets.AnyAsync(t => t.ServiceId == serviceId && t.Status == TicketStatus.Serving)) return OperationResult.Fail("Finish the current visit before calling the next ticket.");
        var day = QueueRules.Day(Now);
        var next = await db.Tickets.Where(t => t.ServiceId == serviceId && t.Day == day && t.Status == TicketStatus.Waiting).OrderBy(t => t.Sequence).FirstOrDefaultAsync();
        if (next is null) return OperationResult.Fail("Nobody is waiting. You're all caught up.");
        next.Status = TicketStatus.Serving;
        next.CalledAt = Now;
        return OperationResult.Ok($"Now calling {next.Code} to counter {service.Counter:D2}.");
    }, staff: true);

    public Task<OperationResult> FinishAsync(ClaimsPrincipal user, int ticketId, TicketStatus outcome) => WriteAsync(user, async (db, id) =>
    {
        var ticket = await db.Tickets.FindAsync(ticketId);
        if (ticket is null || !QueueRules.CanFinish(ticket.Status, outcome)) return OperationResult.Fail("Only a serving ticket can be completed or marked as a no-show.");
        ticket.Status = outcome;
        ticket.FinishedAt = Now;
        return OperationResult.Ok($"Ticket {ticket.Code}: {(outcome == TicketStatus.Completed ? "visit completed" : "visitor did not arrive")}.");
    }, staff: true);

    public Task<OperationResult> SetServiceOpenAsync(ClaimsPrincipal user, int serviceId, bool open) => WriteAsync(user, async (db, id) =>
    {
        var service = await db.Services.FindAsync(serviceId);
        if (service is null) return OperationResult.Fail("Service not found.");
        service.IsOpen = open;
        return OperationResult.Ok($"{service.Name} is now {(open ? "open" : "paused")}.");
    }, staff: true);

    public async Task MaintainAsync()
    {
        await gate.Lock.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var day = QueueRules.Day(Now);
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Tickets.Where(t => t.Day != day && (t.Status == TicketStatus.Waiting || t.Status == TicketStatus.Serving))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TicketStatus.NoShow).SetProperty(t => t.FinishedAt, Now));
            await db.Appointments.Where(a => a.Status == BookingStatus.Reserved && a.StartsAt < Now - 900)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, BookingStatus.Missed));
            await transaction.CommitAsync();
        }
        finally { gate.Lock.Release(); }
        // A single server heartbeat also refreshes time-dependent check-in buttons.
        await changes.PublishAsync();
    }
}
