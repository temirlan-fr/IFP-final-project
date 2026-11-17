using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using QueueLess.Core;

namespace QueueLess.Infrastructure;

public sealed partial class QueueService(IDbContextFactory<QueueDbContext> factory, WriteGate gate, QueueChanges changes, TimeProvider clock)
{
    private long Now => clock.GetUtcNow().ToUnixTimeSeconds();
    public static int UserId(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true && int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    public async Task<List<ServiceSummary>> GetServicesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var day = QueueRules.Day(Now);
        var services = await db.Services.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var active = await db.Tickets.AsNoTracking().Where(x => x.Day == day && (x.Status == TicketStatus.Waiting || x.Status == TicketStatus.Serving)).ToListAsync();
        return services.Select(s => {
            var tickets = active.Where(t => t.ServiceId == s.Id).ToList();
            var serving = tickets.FirstOrDefault(t => t.Status == TicketStatus.Serving);
            var waiting = tickets.Count(t => t.Status == TicketStatus.Waiting);
            return new ServiceSummary(s, waiting, QueueRules.EstimateWait(waiting, s.AverageMinutes, serving is not null), serving?.Code);
        }).ToList();
    }

    public async Task<List<TicketView>> GetMyTicketsAsync(ClaimsPrincipal user)
    {
        var id = UserId(user);
        await using var db = await factory.CreateDbContextAsync();
        var tickets = await db.Tickets.AsNoTracking().Include(x => x.Service).Where(x => x.AccountId == id).OrderByDescending(x => x.Id).Take(20).ToListAsync();
        var day = QueueRules.Day(Now);
        var active = await db.Tickets.AsNoTracking().Where(x => x.Day == day && (x.Status == TicketStatus.Waiting || x.Status == TicketStatus.Serving)).ToListAsync();
        return tickets.Select(t => {
            var ahead = active.Count(a => a.ServiceId == t.ServiceId && a.Status == TicketStatus.Waiting && a.Sequence < t.Sequence);
            var wait = t.Status == TicketStatus.Waiting ? QueueRules.EstimateWait(ahead, t.Service.AverageMinutes, active.Any(a => a.ServiceId == t.ServiceId && a.Status == TicketStatus.Serving)) : 0;
            return new TicketView(t.Id, t.Code, t.Service.Name, t.Status, ahead, wait, t.Service.Counter, t.JoinedAt);
        }).ToList();
    }

    public Task<OperationResult> JoinAsync(ClaimsPrincipal user, int serviceId) => WriteAsync(user, async (db, id) =>
    {
        var service = await db.Services.FindAsync(serviceId);
        if (service is null || !service.IsOpen) return OperationResult.Fail("This service is currently paused. Please choose another service.");
        if (await HasActiveAsync(db, id)) return OperationResult.Fail("You already have an active ticket. Finish or cancel it before joining another queue.");
        var ticket = await NewTicketAsync(db, id, service);
        return OperationResult.Ok($"You're in! Your ticket is {ticket.Code}.");
    });

    public Task<OperationResult> CancelTicketAsync(ClaimsPrincipal user, int ticketId) => WriteAsync(user, async (db, id) =>
    {
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == ticketId && t.AccountId == id);
        if (ticket is null || ticket.Status != TicketStatus.Waiting) return OperationResult.Fail("Only your own waiting ticket can be cancelled.");
        ticket.Status = TicketStatus.Cancelled;
        ticket.FinishedAt = Now;
        return OperationResult.Ok("Your ticket has been cancelled.");
    });

    private static Task<bool> HasActiveAsync(QueueDbContext db, int id) => db.Tickets.AnyAsync(t => t.AccountId == id && (t.Status == TicketStatus.Waiting || t.Status == TicketStatus.Serving));

    private async Task<QueueTicket> NewTicketAsync(QueueDbContext db, int id, ServiceDesk service)
    {
        var day = QueueRules.Day(Now);
        var number = (await db.Tickets.Where(t => t.ServiceId == service.Id && t.Day == day).MaxAsync(t => (int?)t.Sequence) ?? 0) + 1;
        var ticket = new QueueTicket { ServiceId = service.Id, AccountId = id, Sequence = number, Code = $"{service.Prefix}-{number:D3}", Day = day, JoinedAt = Now };
        db.Tickets.Add(ticket);
        return ticket;
    }

    private async Task<OperationResult> WriteAsync(ClaimsPrincipal user, Func<QueueDbContext, int, Task<OperationResult>> action, bool staff = false)
    {
        var id = UserId(user);
        if (id == 0) return OperationResult.Fail("Please sign in to continue.");
        OperationResult result;
        await gate.Lock.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var account = await db.Accounts.FindAsync(id);
            if (account is null || (staff && account.Role != "Operator")) return OperationResult.Fail("You do not have permission for this action.");
            await using var transaction = await db.Database.BeginTransactionAsync();
            result = await action(db, id);
            if (result.Success)
            {
                db.Audit.Add(new AuditEntry { ActorId = id, Action = result.Message, At = Now });
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
        }
        catch (DbUpdateException) { result = OperationResult.Fail("This queue or slot just changed. Refresh and try again."); }
        finally { gate.Lock.Release(); }
        if (result.Success) await changes.PublishAsync();
        return result;
    }
}
