using Microsoft.EntityFrameworkCore;
using QueueLess.Core;

namespace QueueLess.Infrastructure;

public sealed class QueueDbContext(DbContextOptions<QueueDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<ServiceDesk> Services => Set<ServiceDesk>();
    public DbSet<QueueTicket> Tickets => Set<QueueTicket>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>().HasIndex(x => x.Email).IsUnique();
        model.Entity<QueueTicket>().HasIndex(x => new { x.ServiceId, x.Day, x.Sequence }).IsUnique();
        model.Entity<QueueTicket>().HasIndex(x => x.AccountId).IsUnique().HasFilter("Status IN (0, 1)");
        model.Entity<QueueTicket>().HasIndex(x => x.ServiceId).IsUnique().HasFilter("Status = 1");
        model.Entity<QueueTicket>().HasIndex(x => new { x.Day, x.Status, x.ServiceId });
        model.Entity<Appointment>().HasIndex(x => new { x.ServiceId, x.StartsAt }).IsUnique().HasFilter("Status IN (0, 1)");
        model.Entity<Appointment>().HasIndex(x => new { x.AccountId, x.StartsAt }).IsUnique().HasFilter("Status IN (0, 1)");
        model.Entity<Appointment>().HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId);
    }
}
