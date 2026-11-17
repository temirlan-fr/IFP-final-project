using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QueueLess.Core;
using QueueLess.Infrastructure;
namespace QueueLess.Tests;

public sealed class TestClock : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-11-17T10:00:00+05:00");
    public override DateTimeOffset GetUtcNow() => UtcNow;
}
public sealed class TestFactory(DbContextOptions<QueueDbContext> options) : IDbContextFactory<QueueDbContext>
{
    public QueueDbContext CreateDbContext() => new(options);
}
public sealed class QueueTestStore : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"queueless-{Guid.NewGuid():N}.db");
    public TestFactory Factory { get; private set; } = null!;
    public QueueService Service { get; private set; } = null!;
    public TestClock Clock { get; } = new();
    public QueueChanges Changes { get; } = new(NullLogger<QueueChanges>.Instance);
    public ClaimsPrincipal User(int id, string role = "Visitor") => new(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role),
        new Claim("session_expires", Clock.UtcNow.AddDays(10).ToUnixTimeSeconds().ToString())
    }, "Test"));
    public async Task InitializeAsync()
    {
        Factory = new(new DbContextOptionsBuilder<QueueDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
        Service = new(Factory, new WriteGate(), Changes, Clock);
        await using var db = Factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        db.Accounts.AddRange(new Account { Id = 1, Name = "Operator", Email = "operator@test", Role = "Operator" },
            new Account { Id = 2, Name = "Alice", Email = "alice@test" }, new Account { Id = 3, Name = "Bob", Email = "bob@test" });
        db.Services.AddRange(new ServiceDesk { Id = 1, Name = "Student services", Prefix = "S", AverageMinutes = 8, Counter = 1 },
            new ServiceDesk { Id = 2, Name = "IT", Prefix = "T", AverageMinutes = 12, Counter = 2 });
        await db.SaveChangesAsync();
    }
    public Task DisposeAsync() { foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); return Task.CompletedTask; }
}
