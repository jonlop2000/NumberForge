using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NumberForge.Api.Security;

namespace NumberForge.Tests;

public sealed class ApiSecurityTests
{
    [Fact]
    public async Task GuestCookie_IsProtectedHttpOnlyAndReused()
    {
        await using var host = await SecurityHost.Start();
        var first = await host.Client.GetAsync("/api/security/player");
        var firstId = await first.Content.ReadAsStringAsync();
        string cookie = first.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(firstId.Trim('"'), cookie);

        host.Client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        var again = await host.Client.GetAsync("/api/security/player");
        Assert.Equal(firstId, await again.Content.ReadAsStringAsync());
        Assert.False(again.Headers.Contains("Set-Cookie")); // A fixed expiry does not extend on every request.
    }

    [Fact]
    public async Task TamperedCookie_CannotRecoverTheOriginalIdentity()
    {
        await using var host = await SecurityHost.Start();
        var first = await host.Client.GetAsync("/api/security/player");
        string original = await first.Content.ReadAsStringAsync();
        string cookie = first.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        int index = cookie.IndexOf('=') + 20;
        cookie = cookie[..index] + (cookie[index] == 'A' ? "B" : "A") + cookie[(index + 1)..];
        host.Client.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.NotEqual(original, await host.Client.GetStringAsync("/api/security/player"));
    }

    [Fact]
    public async Task Cookie_ExpiresAfterThirtyDays()
    {
        var clock = new SecurityClock();
        await using var host = await SecurityHost.Start(clock: clock);
        var first = await host.Client.GetAsync("/api/security/player");
        host.Client.DefaultRequestHeaders.Add("Cookie", first.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        clock.Advance(TimeSpan.FromDays(31));
        Assert.NotEqual(await first.Content.ReadAsStringAsync(), await host.Client.GetStringAsync("/api/security/player"));
    }

    [Theory]
    [InlineData("https://attacker.example", null)]
    [InlineData("null", null)]
    [InlineData("http://localhost:5100.attacker.example", null)]
    [InlineData("http://localhost:5100", "cross-site")]
    [InlineData(null, "same-site")]
    [InlineData(null, null)]
    public async Task UntrustedMutation_IsRejectedBeforeIssuingCookie(string? origin, string? fetchSite)
    {
        await using var host = await SecurityHost.Start();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sessions");
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (fetchSite is not null) request.Headers.Add("Sec-Fetch-Site", fetchSite);
        var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("Origin", "http://localhost:5100")]
    [InlineData("Sec-Fetch-Site", "same-origin")]
    public async Task TrustedBodylessSessionCreation_WorksWithoutFrontendChanges(string header, string value)
    {
        await using var host = await SecurityHost.Start();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sessions");
        request.Headers.Add(header, value);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task NonJsonMutation_IsRejected(string contentType)
    {
        await using var host = await SecurityHost.Start();
        host.Client.DefaultRequestHeaders.Add("Origin", "http://localhost:5100");
        var response = await host.Client.PostAsync("/api/security/body", new StringContent("operationId=1", Encoding.UTF8, contentType));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"operationId\":\"1\"}")]
    [InlineData("{\"operationId\":1,\"isWon\":true}")]
    [InlineData("{\"operationId\":2147483648}")]
    public async Task MalformedOrUnexpectedJson_IsRejected(string json)
    {
        await using var host = await SecurityHost.Start();
        host.Client.DefaultRequestHeaders.Add("Origin", "http://localhost:5100");
        var response = await host.Client.PostAsync("/api/security/body", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ValidJson_ReachesModelBindingAfterBodyInspection()
    {
        await using var host = await SecurityHost.Start();
        host.Client.DefaultRequestHeaders.Add("Origin", "http://localhost:5100");
        var response = await host.Client.PostAsJsonAsync("/api/security/body", new { operationId = 2 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedBodies_WithOrWithoutContentLength_AreRejected(bool omitLength)
    {
        await using var host = await SecurityHost.Start();
        host.Client.DefaultRequestHeaders.Add("Origin", "http://localhost:5100");
        using HttpContent body = omitLength
            ? new UnknownLengthContent(new string('x', 4097))
            : new StringContent(new string('x', 4097), Encoding.UTF8, "application/json");
        var response = await host.Client.PostAsync("/api/security/body", body);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task PerIpLimits_ReturnRetryAfter_AndResetWithTime()
    {
        var clock = new SecurityClock();
        await using var host = await SecurityHost.Start(new() { ["ApiSecurity:RequestsPerMinute"] = "2" }, clock);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/security/player")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/security/player")).StatusCode);
        // An untrusted forwarded address must not give callers a fresh rate-limit bucket.
        host.Client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.18");
        var limited = await host.Client.GetAsync("/api/security/player");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), limited.Headers.RetryAfter?.Delta);
        Assert.Contains("application/problem+json", limited.Content.Headers.ContentType?.ToString());
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/security/player")).StatusCode);
    }

    [Fact]
    public async Task SessionCreation_HasATighterLimitThanReading()
    {
        await using var host = await SecurityHost.Start(new() { ["ApiSecurity:CreationsPerMinute"] = "1" });
        host.Client.DefaultRequestHeaders.Add("Origin", "http://localhost:5100");
        Assert.Equal(HttpStatusCode.Created, (await host.Client.PostAsync("/api/sessions", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.Client.PostAsync("/api/sessions/", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/security/player")).StatusCode);
    }

    [Fact]
    public async Task UnexpectedExceptions_ReturnGenericProblemDetailsAndSecurityHeaders()
    {
        await using var host = await SecurityHost.Start();
        var response = await host.Client.GetAsync("/api/security/failure");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("super-secret", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StorageFailure_ReturnsRetryableProblemWithoutProviderDetails(bool wrapped)
    {
        await using var host = await SecurityHost.Start();
        var response = await host.Client.GetAsync($"/api/security/database-failure?wrapped={wrapped}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter?.Delta);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("super-secret", body);
        Assert.DoesNotContain(nameof(SecurityProbeDbException), body);
    }

    [Fact]
    public async Task Production_RequiresHttpsAndIssuesSecureHostCookie()
    {
        string keyPath = NewKeyPath();
        try
        {
            await using var host = await SecurityHost.Start(ProductionSettings(keyPath), production: true);
            host.Client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("http://game.example/api/security/player")).StatusCode);
            var response = await host.Client.GetAsync("https://game.example/api/security/player");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string cookie = response.Headers.GetValues("Set-Cookie").Single();
            Assert.StartsWith("__Host-numberforge.player=", cookie);
            Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.True(response.Headers.Contains("Strict-Transport-Security"));
        }
        finally { Directory.Delete(keyPath, recursive: true); }
    }

    [Fact]
    public async Task PersistedKeys_PreserveIdentityAcrossServerRestarts()
    {
        string keyPath = NewKeyPath();
        try
        {
            string cookie;
            string id;
            var settings = new Dictionary<string, string?> { ["ApiSecurity:DataProtectionKeyPath"] = keyPath };
            await using (var first = await SecurityHost.Start(settings))
            {
                var response = await first.Client.GetAsync("/api/security/player");
                id = await response.Content.ReadAsStringAsync();
                cookie = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            }
            await using var restarted = await SecurityHost.Start(settings);
            restarted.Client.DefaultRequestHeaders.Add("Cookie", cookie);
            Assert.Equal(id, await restarted.Client.GetStringAsync("/api/security/player"));
        }
        finally { Directory.Delete(keyPath, recursive: true); }
    }

    [Theory]
    [InlineData("AllowedHosts", "*")]
    [InlineData("ApiSecurity:AllowedOrigins:0", "http://game.example")]
    [InlineData("ApiSecurity:DataProtectionKeyPath", "relative-path")]
    [InlineData("FORWARDEDHEADERS_ENABLED", "true")]
    public async Task UnsafeProductionConfiguration_FailsAtStartup(string key, string value)
    {
        string keyPath = NewKeyPath();
        try
        {
            var settings = ProductionSettings(keyPath);
            settings[key] = value;
            await Assert.ThrowsAsync<InvalidOperationException>(() => SecurityHost.Start(settings, production: true));
        }
        finally { Directory.Delete(keyPath, recursive: true); }
    }

    [Fact]
    public void Limiter_BoundsAddressStateAndConcurrentWork()
    {
        var clock = new SecurityClock();
        using var limiter = new ApiRequestLimiter(new ApiSecurityOptions { MaxClientBuckets = 2, MaxConcurrentRequests = 1 }, clock);
        var first = Context("192.0.2.1");
        Assert.True(limiter.TryAcquire(first, out var lease, out _));
        Assert.False(limiter.TryAcquire(Context("192.0.2.2"), out _, out _));
        lease!.Dispose();
        Assert.True(limiter.TryAcquire(first, out lease, out _));
        lease!.Dispose();
        Assert.False(limiter.TryAcquire(Context("192.0.2.3"), out _, out _));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(limiter.TryAcquire(Context("192.0.2.3"), out lease, out _));
        lease!.Dispose();
    }

    private static DefaultHttpContext Context(string ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Request.Method = "GET";
        return context;
    }

    private static string NewKeyPath()
    {
        string path = Path.Combine(Path.GetTempPath(), "numberforge-security-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static Dictionary<string, string?> ProductionSettings(string keyPath) => new()
    {
        ["AllowedHosts"] = "game.example",
        ["ApiSecurity:AllowedOrigins:0"] = "https://game.example",
        ["ApiSecurity:DataProtectionKeyPath"] = keyPath
    };

    // These probes exercise the real HTTP middleware without depending on any particular puzzle or database.
    private sealed class SecurityHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public static async Task<SecurityHost> Start(Dictionary<string, string?>? settings = null, TimeProvider? clock = null, bool production = false)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = production ? "Production" : "Development",
                ApplicationName = typeof(ApiSecurityTests).Assembly.GetName().Name
            });
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
            builder.WebHost.UseTestServer();
            if (clock is not null) builder.Services.AddSingleton(clock);
            builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.ApplicationParts.Add(new AssemblyPart(typeof(SecurityProbeController).Assembly));
            });
            builder.AddApiSecurity();
            var app = builder.Build();
            app.UseApiSecurity();
            app.MapControllers();
            app.MapGet("/api/security/player", (HttpContext context) => PlayerIdentity.GetRequiredId(context.User)).RequireAuthorization();
            app.MapPost("/api/sessions", () => Results.StatusCode(201)).RequireAuthorization();
            await app.StartAsync();
            var client = app.GetTestClient();
            client.BaseAddress = new Uri("http://localhost:5100");
            return new SecurityHost(app, client);
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); }
    }

    private sealed class SecurityClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) { _now += duration; _timestamp += duration.Ticks; }
    }

    private sealed class UnknownLengthContent(string body) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(Encoding.UTF8.GetBytes(body)).AsTask();
    }
}

[ApiController]
[Route("api/security")]
public sealed class SecurityProbeController : ControllerBase
{
    [HttpPost("body")]
    public ActionResult<int> Body(SecurityProbeRequest request) => Ok(request.OperationId);

    [HttpGet("failure")]
    public IActionResult Failure() => throw new InvalidOperationException("super-secret connection string");

    [HttpGet("database-failure")]
    public IActionResult DatabaseFailure(bool wrapped)
    {
        var error = new SecurityProbeDbException();
        throw wrapped ? new InvalidOperationException("super-secret provider wrapper", error) : error;
    }
}

public sealed record SecurityProbeRequest(int OperationId);
public sealed class SecurityProbeDbException() : DbException("super-secret database details");
