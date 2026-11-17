using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using QueueLess.Core;
namespace QueueLess.Infrastructure;

public sealed partial class QueueService
{
    public async Task<List<Appointment>> GetAppointmentsAsync(ClaimsPrincipal user)
    {
        await using var db = await factory.CreateDbContextAsync();
        var id = UserId(user);
        return await db.Appointments.AsNoTracking().Include(a => a.Service).Where(a => a.AccountId == id).OrderByDescending(a => a.StartsAt).Take(30).ToListAsync();
    }
    public async Task<List<long>> GetAvailableSlotsAsync(int serviceId, DateOnly date)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Services.AnyAsync(s => s.Id == serviceId && s.IsOpen)) return [];
        var start = new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(5)).ToUnixTimeSeconds();
        var end = start + 9 * 3600;
        var taken = await db.Appointments.AsNoTracking().Where(a => a.ServiceId == serviceId && a.StartsAt >= start && a.StartsAt < end && (a.Status == BookingStatus.Reserved || a.Status == BookingStatus.CheckedIn)).Select(a => a.StartsAt).ToListAsync();
        return Enumerable.Range(0, 18).Select(i => start + i * 1800).Where(t => QueueRules.ValidSlot(t, Now) && !taken.Contains(t)).ToList();
    }
    public Task<OperationResult> BookAsync(ClaimsPrincipal user, int serviceId, long slot) => WriteAsync(user, async (db, id) =>
    {
        if (!QueueRules.ValidSlot(slot, Now)) return OperationResult.Fail("Choose a future half-hour slot between 09:00 and 17:30 within the next seven days.");
        if (!await db.Services.AnyAsync(s => s.Id == serviceId && s.IsOpen)) return OperationResult.Fail("This service is paused. Please choose another service.");
        if (await db.Appointments.AnyAsync(a => (a.ServiceId == serviceId || a.AccountId == id) && a.StartsAt == slot && (a.Status == BookingStatus.Reserved || a.Status == BookingStatus.CheckedIn))) return OperationResult.Fail("This slot is no longer available, or you already have an appointment at that time.");
        if (await db.Appointments.CountAsync(a => a.AccountId == id && a.Status == BookingStatus.Reserved && a.StartsAt > Now - 900) >= 3) return OperationResult.Fail("You can hold up to three upcoming appointments. Cancel one before booking another.");
        db.Appointments.Add(new Appointment { ServiceId = serviceId, AccountId = id, StartsAt = slot });
        return OperationResult.Ok("Your visit is booked. Check in from 10 minutes before your appointment.");
    });
    public Task<OperationResult> CancelBookingAsync(ClaimsPrincipal user, int appointmentId) => WriteAsync(user, async (db, id) =>
    {
        var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == appointmentId && a.AccountId == id);
        if (appointment is null || appointment.Status != BookingStatus.Reserved) return OperationResult.Fail("Only your own reserved appointment can be cancelled.");
        appointment.Status = BookingStatus.Cancelled;
        return OperationResult.Ok("Your appointment has been cancelled. The time slot is available again.");
    });
    public Task<OperationResult> CheckInAsync(ClaimsPrincipal user, int appointmentId) => WriteAsync(user, async (db, id) =>
    {
        var appointment = await db.Appointments.Include(a => a.Service).SingleOrDefaultAsync(a => a.Id == appointmentId && a.AccountId == id);
        if (appointment is null || appointment.Status != BookingStatus.Reserved) return OperationResult.Fail("This appointment cannot be checked in.");
        if (!QueueRules.CanCheckIn(appointment.StartsAt, Now)) return OperationResult.Fail("Check-in opens 10 minutes before and closes 15 minutes after your appointment.");
        if (!appointment.Service.IsOpen) return OperationResult.Fail("This desk is paused. Please ask the service centre for help.");
        if (await HasActiveAsync(db, id)) return OperationResult.Fail("Please finish or cancel your active ticket before checking in.");
        var ticket = await NewTicketAsync(db, id, appointment.Service);
        appointment.Status = BookingStatus.CheckedIn;
        return OperationResult.Ok($"Checked in! Your live queue ticket is {ticket.Code}.");
    });
}
