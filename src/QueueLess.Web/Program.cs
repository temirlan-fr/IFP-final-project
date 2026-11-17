using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using QueueLess.Core;
using QueueLess.Infrastructure;
using QueueLess.Web.Components;
using QueueLess.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var httpsTermination = builder.Configuration.GetValue<bool>("Hosting:HttpsTermination");
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/login";
    options.Cookie.Name = "QueueLess.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("accounts", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var dataDirectory = builder.Configuration["Storage:DataDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys"))).SetApplicationName("QueueLess");
builder.Services.AddDbContextFactory<QueueDbContext>(o => o.UseSqlite(
    builder.Configuration.GetConnectionString("QueueLess") ?? $"Data Source={Path.Combine(dataDirectory, "queueless.db")}"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WriteGate>();
builder.Services.AddSingleton<QueueChanges>();
builder.Services.AddScoped<QueueService>();
builder.Services.AddHostedService<QueueMaintenance>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();

var app = builder.Build();
// Enable only when all public traffic enters through an HTTPS-terminating edge.
// Do not trust client-supplied forwarded headers or use them for rate-limit identity.
if (httpsTermination)
    app.Use((context, next) => { context.Request.Scheme = "https"; return next(context); });
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    if (!httpsTermination) app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapPost("/account/{action}", async (string action, HttpContext context, IAntiforgery antiforgery, AccountService accounts) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest("Your session changed. Reload the form and try again."); }
    if (action == "logout")
    {
        await context.SignOutAsync();
        return Results.LocalRedirect("/");
    }
    if (action is not ("login" or "register")) return Results.NotFound();
    var form = await context.Request.ReadFormAsync();
    var account = action == "login"
        ? await accounts.LoginAsync(form["email"].ToString(), form["password"].ToString())
        : await accounts.RegisterAsync(form["name"].ToString(), form["email"].ToString(), form["password"].ToString());
    if (account is null) return Results.LocalRedirect(action == "login" ? "/login?error=credentials" : "/register?error=registration");
    var identity = new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
        new Claim(ClaimTypes.Name, account.Name), new Claim(ClaimTypes.Role, account.Role),
        new Claim("session_expires", DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds().ToString())
    }, CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(new ClaimsPrincipal(identity));
    return Results.LocalRedirect(account.Role == "Operator" ? "/operator" : "/");
}).RequireRateLimiting("accounts");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/api/services", async (QueueService queues) => Results.Ok(await queues.GetServicesAsync()));
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await DatabaseSeed.InitializeAsync(app.Services, app.Environment.IsDevelopment());
app.Run();

public partial class Program { }
