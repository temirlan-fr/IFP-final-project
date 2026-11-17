using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueueLess.Infrastructure;
using QueueLess.Web.Services;
namespace QueueLess.Tests;

public sealed class QueueWebFactory : WebApplicationFactory<Program>
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"queueless-http-{Guid.NewGuid():N}.db");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:QueueLess"] = $"Data Source={path};Pooling=False"
        }));
    }
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }
}

public class HttpFlowTests
{
    private static HttpClient Client(QueueWebFactory app) => app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
    private static async Task<string> Token(HttpClient client, string page)
    {
        var html = await client.GetStringAsync(page);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Expected an antiforgery token on the account form");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static Task<HttpResponseMessage> Submit(HttpClient client, string path, params (string, string)[] values) =>
        client.PostAsync(path, new FormUrlEncodedContent(values.Select(v => new KeyValuePair<string, string>(v.Item1, v.Item2))));

    [Theory]
    [InlineData("/", "Your day, without the delay")]
    [InlineData("/login", "Welcome back")]
    [InlineData("/register", "Create your account")]
    [InlineData("/display", "A little closer to your turn")]
    [InlineData("/help", "A few things worth knowing")]
    public async Task PublicPagesRenderTheirExpectedContent(string path, string expected)
    {
        await using var app = new QueueWebFactory(); using var client = Client(app);
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RegistrationLoginAndLogoutWorkThroughRealHttpCookies()
    {
        await using var app = new QueueWebFactory(); using var client = Client(app);
        var token = await Token(client, "/register");
        var response = await Submit(client, "/account/register", ("__RequestVerificationToken", token), ("name", "Test Student"), ("email", "new@example.test"), ("password", "StrongPassword123"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var html = await client.GetStringAsync("/tickets");
        Assert.Contains("Test Student", html); Assert.Contains("My tickets", html);
        token = await Token(client, "/tickets");
        Assert.Equal(HttpStatusCode.Redirect, (await Submit(client, "/account/logout", ("__RequestVerificationToken", token))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/tickets")).StatusCode);
        token = await Token(client, "/login");
        response = await Submit(client, "/account/login", ("__RequestVerificationToken", token), ("email", "NEW@example.test"), ("password", "StrongPassword123"));
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Contains("Test Student", await client.GetStringAsync("/appointments"));
    }

    [Fact]
    public async Task OperatorLoginCanOpenDeskButStudentCannot()
    {
        await using var app = new QueueWebFactory(); using var staff = Client(app); using var student = Client(app);
        var token = await Token(staff, "/login");
        var result = await Submit(staff, "/account/login", ("__RequestVerificationToken", token), ("email", "operator@queueless.demo"), ("password", "QueueLess2026!"));
        Assert.Equal("/operator", result.Headers.Location?.OriginalString);
        Assert.Contains("Call next visitor", await staff.GetStringAsync("/operator"));
        token = await Token(student, "/login");
        await Submit(student, "/account/login", ("__RequestVerificationToken", token), ("email", "student@queueless.demo"), ("password", "QueueLess2026!"));
        Assert.Equal(HttpStatusCode.Redirect, (await student.GetAsync("/operator")).StatusCode);
    }

    [Fact]
    public async Task ForgedAccountPostWithoutCsrfIsRejected()
    {
        await using var app = new QueueWebFactory(); using var client = Client(app);
        var result = await Submit(client, "/account/login", ("email", "operator@queueless.demo"), ("password", "QueueLess2026!"));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task WrongPasswordAndInvalidRegistrationAreRejected()
    {
        await using var app = new QueueWebFactory(); using var client = Client(app);
        var token = await Token(client, "/login");
        var result = await Submit(client, "/account/login", ("__RequestVerificationToken", token), ("email", "student@queueless.demo"), ("password", "wrong"));
        Assert.Equal("/login?error=credentials", result.Headers.Location?.OriginalString);
        token = await Token(client, "/register");
        result = await Submit(client, "/account/register", ("__RequestVerificationToken", token), ("name", "X"), ("email", "bad"), ("password", "weak"));
        Assert.Equal("/register?error=registration", result.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PasswordsAreHashedAndPublicApiExcludesAccounts()
    {
        await using var app = new QueueWebFactory(); using var client = Client(app);
        var json = await client.GetStringAsync("/api/services");
        Assert.Contains("Student services", json);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("queueless.demo", json);
        using var scope = app.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var account = await accounts.RegisterAsync("Hash Test", "hash@test.local", "LongPassword123");
        Assert.NotNull(account); Assert.NotEqual("LongPassword123", account.PasswordHash);
        Assert.Null(await accounts.RegisterAsync("Duplicate", "HASH@test.local", "LongPassword123"));
    }

    [Fact]
    public async Task UnknownRouteReturns404AndHealthIsAvailable()
    {
        await using var app = new QueueWebFactory(); using var client = Client(app);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/not-a-real-page")).StatusCode);
        Assert.Contains("healthy", await client.GetStringAsync("/health"));
    }
}
