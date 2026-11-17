using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QueueLess.Core;
using QueueLess.Infrastructure;

namespace QueueLess.Web.Services;
public sealed class AccountService(IDbContextFactory<QueueDbContext> factory, IPasswordHasher<Account> hasher)
{
    public async Task<Account?> LoginAsync(string email, string password)
    {
        if (email.Length > 254 || password.Length > 128) return null;
        await using var db = await factory.CreateDbContextAsync();
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Email == email.Trim().ToLowerInvariant());
        if (account is null) return null;
        var verification = hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed) return null;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = hasher.HashPassword(account, password);
            await db.SaveChangesAsync();
        }
        return account;
    }
    public async Task<Account?> RegisterAsync(string name, string email, string password)
    {
        name = name.Trim(); email = email.Trim().ToLowerInvariant();
        if (name.Length is < 2 or > 60 || email.Length > 254 || !new EmailAddressAttribute().IsValid(email)
            || password.Length is < 10 or > 128 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return null;
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Accounts.AnyAsync(a => a.Email == email)) return null;
        var account = new Account { Name = name, Email = email };
        account.PasswordHash = hasher.HashPassword(account, password);
        db.Accounts.Add(account);
        try { await db.SaveChangesAsync(); return account; }
        catch (DbUpdateException) { return null; }
    }
}
