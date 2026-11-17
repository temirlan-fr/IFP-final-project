using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueueLess.Core;
using QueueLess.Infrastructure;
using QueueLess.Web.Services;

namespace QueueLess.Tests;

public class ProductionHostingTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"queueless-production-{Guid.NewGuid():N}");
    private const string Email = "private.operator@example.test";
    private const string Password = "TestOnlyPrivate123!";

    private ServiceProvider Services(string? email, string? password)
    {
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Bootstrap:OperatorEmail"] = email, ["Bootstrap:OperatorPassword"] = password
        }).Build());
        services.AddDbContextFactory<QueueDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(directory, "seed.db")};Pooling=False"));
        services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ProductionWithoutBootstrapCreatesServicesButNoDemoAccounts()
    {
        await using var services = Services(null, null);
        await DatabaseSeed.InitializeAsync(services, false);
        await using var db = await services.GetRequiredService<IDbContextFactory<QueueDbContext>>().CreateDbContextAsync();
        Assert.Equal(4, await db.Services.CountAsync());
        Assert.Empty(await db.Accounts.ToListAsync());
    }

    [Theory]
    [InlineData("invalid", Password)]
    [InlineData(null, Password)]
    [InlineData(Email, null)]
    [InlineData(Email, "short1")]
    [InlineData(Email, "QueueLess2026!")]
    public async Task InvalidBootstrapStopsStartup(string? email, string? password)
    {
        await using var services = Services(email, password);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseSeed.InitializeAsync(services, false));
    }

    [Fact]
    public async Task BootstrapCannotPromoteAnExistingVisitor()
    {
        await using var services = Services(Email, Password);
        await using var db = await services.GetRequiredService<IDbContextFactory<QueueDbContext>>().CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        db.Accounts.Add(new Account { Name = "Visitor", Email = Email });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseSeed.InitializeAsync(services, false));
        Assert.Equal("Visitor", (await db.Accounts.SingleAsync()).Role);
    }

    [Fact]
    public async Task ProductionLoginUsesSecureCookiesAndSurvivesHostRestart()
    {
        string cookie;
        string passwordHash;
        await using (var app = new ProductionFactory(directory, Password))
        {
            using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            var html = await client.GetStringAsync("/login");
            var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string> {
                ["email"] = Email, ["password"] = Password, ["__RequestVerificationToken"] = token
            }));
            Assert.Equal("/operator", response.Headers.Location?.OriginalString);
            var header = Assert.Single(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith("QueueLess.Session="));
            Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
            cookie = header.Split(';')[0];
            await using var db = await app.Services.GetRequiredService<IDbContextFactory<QueueDbContext>>().CreateDbContextAsync();
            var account = Assert.Single(await db.Accounts.ToListAsync());
            Assert.Equal("Operator", account.Role);
            passwordHash = account.PasswordHash;
            Assert.NotEqual(Password, passwordHash);
        }
        Assert.True(File.Exists(Path.Combine(directory, "queueless.db")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(directory, "keys"), "*.xml"));
        // Existing accounts must not be reset by a later bootstrap configuration.
        await using var restarted = new ProductionFactory(directory, "ChangedTestOnly456!");
        using var afterRestart = restarted.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        afterRestart.DefaultRequestHeaders.Add("Cookie", cookie);
        var page = await afterRestart.GetAsync("/operator");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Call next visitor", await page.Content.ReadAsStringAsync());
        await using var check = await restarted.Services.GetRequiredService<IDbContextFactory<QueueDbContext>>().CreateDbContextAsync();
        Assert.Equal(passwordHash, (await check.Accounts.SingleAsync()).PasswordHash);
        using var internalProbe = restarted.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await internalProbe.GetAsync("/health")).StatusCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private sealed class ProductionFactory(string directory, string password) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Storage:DataDirectory", directory);
            builder.UseSetting("Hosting:HttpsTermination", "true");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Storage:DataDirectory"] = directory,
                ["ConnectionStrings:QueueLess"] = $"Data Source={Path.Combine(directory, "queueless.db")};Pooling=False",
                ["Hosting:HttpsTermination"] = "true",
                ["Bootstrap:OperatorEmail"] = Email,
                ["Bootstrap:OperatorPassword"] = password
            }));
        }
    }
}
