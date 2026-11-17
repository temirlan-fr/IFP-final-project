using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QueueLess.Core;
using QueueLess.Infrastructure;

namespace QueueLess.Web.Services;
public static class DatabaseSeed
{
    public static async Task InitializeAsync(IServiceProvider services, bool development)
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<QueueDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        if (!await db.Services.AnyAsync())
        {
            db.Services.AddRange(
                new ServiceDesk { Name = "Student services", Description = "Enrollment letters, student IDs and academic records.", Prefix = "S", Category = "Academic", AverageMinutes = 8, Counter = 1 },
                new ServiceDesk { Name = "IT help desk", Description = "Campus Wi-Fi, account access and technical support.", Prefix = "T", Category = "Technology", AverageMinutes = 12, Counter = 2 },
                new ServiceDesk { Name = "Finance office", Description = "Tuition payments, scholarships and financial documents.", Prefix = "F", Category = "Finance", AverageMinutes = 10, Counter = 3 },
                new ServiceDesk { Name = "Library support", Description = "Library membership, book renewals and research access.", Prefix = "L", Category = "Library", AverageMinutes = 5, Counter = 4 });
            await db.SaveChangesAsync();
        }
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>();
        if (!development)
        {
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var email = config["Bootstrap:OperatorEmail"]?.Trim().ToLowerInvariant();
            var password = config["Bootstrap:OperatorPassword"];
            if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(password)) return;
            if (string.IsNullOrEmpty(email) || email.Length > 254 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
                throw new InvalidOperationException("Configure a valid Bootstrap:OperatorEmail.");
            var existing = await db.Accounts.SingleOrDefaultAsync(a => a.Email == email);
            if (existing is not null)
            {
                if (existing.Role != "Operator") throw new InvalidOperationException("The bootstrap email belongs to a visitor. Choose a different email.");
                return;
            }
            if (password is null || password.Length is < 12 or > 128 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit)
                || password == "QueueLess2026!")
                throw new InvalidOperationException("Configure a private Bootstrap:OperatorPassword with 12 to 128 characters, letters and digits. Do not use the demo password.");
            var account = new Account { Name = "Campus Operator", Email = email, Role = "Operator" };
            account.PasswordHash = hasher.HashPassword(account, password);
            db.Accounts.Add(account);
            await db.SaveChangesAsync();
            return;
        }
        foreach (var account in new[] {
            new Account { Name = "Campus Operator", Email = "operator@queueless.demo", Role = "Operator" },
            new Account { Name = "Demo Student", Email = "student@queueless.demo" }
        })
        {
            if (await db.Accounts.AnyAsync(a => a.Email == account.Email)) continue;
            account.PasswordHash = hasher.HashPassword(account, "QueueLess2026!");
            db.Accounts.Add(account);
        }
        await db.SaveChangesAsync();
    }
}
