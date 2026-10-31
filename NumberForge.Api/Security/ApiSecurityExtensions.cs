using System.Net;
using System.Data.Common;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NumberForge.Api.Security;

public static class ApiSecurityExtensions
{
    public static WebApplicationBuilder AddApiSecurity(this WebApplicationBuilder builder)
    {
        var options = new ApiSecurityOptions();
        builder.Configuration.GetSection(ApiSecurityOptions.SectionName).Bind(options);
        if (builder.Environment.IsDevelopment() && options.AllowedOrigins.Length == 0)
        {
            options.AllowedOrigins = new[] { "http://localhost:5173", "http://localhost:5100",
                "http://127.0.0.1:5173", "http://127.0.0.1:5100" };
        }
        Validate(options, builder);
        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ApiRequestLimiter>();
        builder.Services.AddProblemDetails();

        builder.WebHost.ConfigureKestrel(server =>
        {
            server.AddServerHeader = false;
            server.Limits.MaxConcurrentConnections = 512;
            server.Limits.MaxRequestBodySize = options.MaxRequestBodyBytes;
            server.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            server.Limits.MaxRequestHeaderCount = 32;
            server.Limits.MaxRequestLineSize = 4096;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
        });
        builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(json =>
        {
            json.JsonSerializerOptions.MaxDepth = 16;
            json.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            json.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        });

        var protection = builder.Services.AddDataProtection().SetApplicationName("NumberForge");
        if (!string.IsNullOrWhiteSpace(options.DataProtectionKeyPath))
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(options.DataProtectionKeyPath));
        }
        if (!string.IsNullOrWhiteSpace(options.DataProtectionCertificatePath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(options.DataProtectionCertificatePath,
                options.DataProtectionCertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey)
            {
                throw new InvalidOperationException("The Data Protection certificate must include its private key.");
            }
            protection.ProtectKeysWithCertificate(certificate);
        }

        builder.Services.AddAuthentication(PlayerIdentity.Scheme).AddCookie(PlayerIdentity.Scheme, cookie =>
        {
            cookie.Cookie.Name = builder.Environment.IsDevelopment()
                ? PlayerIdentity.DevelopmentCookieName : PlayerIdentity.ProductionCookieName;
            cookie.Cookie.Path = "/";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Strict;
            cookie.Cookie.IsEssential = true;
            cookie.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            cookie.ExpireTimeSpan = TimeSpan.FromDays(30);
            cookie.SlidingExpiration = false;
            cookie.Events.OnRedirectToLogin = context => Results.Problem(statusCode: 401, title: "Authentication is required.")
                .ExecuteAsync(context.HttpContext);
            cookie.Events.OnRedirectToAccessDenied = context => Results.Problem(statusCode: 403, title: "Access is denied.")
                .ExecuteAsync(context.HttpContext);
        });
        builder.Services.AddOptions<CookieAuthenticationOptions>(PlayerIdentity.Scheme)
            .Configure<TimeProvider>((cookie, clock) => cookie.TimeProvider = clock);
        builder.Services.AddAuthorization();

        // Only explicitly listed proxy addresses may supply forwarding headers. Never trust arbitrary X-Forwarded-* input.
        if (options.KnownProxies.Length > 0)
        {
            builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
            {
                forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                forwarded.ForwardLimit = 1;
                forwarded.KnownIPNetworks.Clear();
                forwarded.KnownProxies.Clear();
                foreach (string address in options.KnownProxies) forwarded.KnownProxies.Add(IPAddress.Parse(address));
            });
        }
        return builder;
    }

    public static WebApplication UseApiSecurity(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<ApiSecurityOptions>();
        if (options.KnownProxies.Length > 0) app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                    context.Response.Headers["X-Frame-Options"] = "DENY";
                    context.Response.Headers["Referrer-Policy"] = "no-referrer";
                    context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
                    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
                    if (!app.Environment.IsDevelopment() && context.Request.IsHttps)
                    {
                        context.Response.Headers.StrictTransportSecurity = "max-age=15552000";
                    }
                    return Task.CompletedTask;
                });
            }
            await next(context);
        });
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = async context =>
            {
                // Do not serialize exception messages, stack traces, connection strings, or filesystem paths.
                var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                bool databaseUnavailable = HasDatabaseException(error);
                int status = error is BadHttpRequestException badRequest ? badRequest.StatusCode : databaseUnavailable ? 503 : 500;
                if (databaseUnavailable) context.Response.Headers.RetryAfter = "5";
                await Results.Problem(statusCode: status, title: status == 413 ? "Request body is too large."
                    : status < 500 ? "The request could not be processed."
                    : status == 503 ? "Game storage is temporarily unavailable. Please try again shortly."
                    : "An unexpected server error occurred.")
                    .ExecuteAsync(context);
            }
        });
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api")) { await next(context); return; }
            var limiter = context.RequestServices.GetRequiredService<ApiRequestLimiter>();
            if (!limiter.TryAcquire(context, out var lease, out int retryAfter))
            {
                context.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await Results.Problem(statusCode: 429, title: "Too many requests. Please try again shortly.").ExecuteAsync(context);
                return;
            }
            using (lease) await next(context);
        });
        app.UseMiddleware<ApiRequestGuardMiddleware>();
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && context.User.Identity?.IsAuthenticated != true)
            {
                // Guest play needs no signup. The protected cookie is the credential; session IDs alone grant no access.
                string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                var principal = PlayerIdentity.CreatePrincipal(id);
                await context.SignInAsync(PlayerIdentity.Scheme, principal, new AuthenticationProperties { IsPersistent = true });
                context.User = principal;
            }
            await next(context);
        });
        app.UseAuthorization();
        return app;
    }

    private static bool HasDatabaseException(Exception? exception)
    {
        // EF can wrap a provider failure. Clients still receive a generic retryable response.
        for (int depth = 0; exception is not null && depth < 8; depth++, exception = exception.InnerException)
            if (exception is DbException) return true;
        return false;
    }

    private static void Validate(ApiSecurityOptions options, WebApplicationBuilder builder)
    {
        options.AllowedOrigins = options.AllowedOrigins.Select(origin => ApiRequestGuardMiddleware.NormalizeOrigin(origin)
            ?? throw new InvalidOperationException("ApiSecurity:AllowedOrigins must contain exact HTTP(S) origins without paths or wildcards."))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (options.AllowedOrigins.Length == 0)
        {
            throw new InvalidOperationException("Configure at least one ApiSecurity:AllowedOrigins entry.");
        }
        foreach (string proxy in options.KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out _)) throw new InvalidOperationException("ApiSecurity:KnownProxies must contain IP addresses.");
        }
        if (!builder.Environment.IsDevelopment())
        {
            // The automatic hosting switch can trust forwarding headers from every address.
            // Our explicit KnownProxies configuration must remain the only source of that trust.
            if (bool.TryParse(builder.Configuration["FORWARDEDHEADERS_ENABLED"], out bool automaticForwarding) && automaticForwarding)
                throw new InvalidOperationException("Disable ASPNETCORE_FORWARDEDHEADERS_ENABLED and configure ApiSecurity:KnownProxies instead.");
            if (options.AllowedOrigins.Any(origin => !origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Production allowed origins must use HTTPS.");
            var hosts = builder.Configuration["AllowedHosts"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (hosts is null || hosts.Length == 0 || hosts.Any(host => host.Contains('*')))
                throw new InvalidOperationException("Configure explicit AllowedHosts before running outside Development.");
            if (string.IsNullOrWhiteSpace(options.DataProtectionKeyPath) || !Path.IsPathFullyQualified(options.DataProtectionKeyPath))
                throw new InvalidOperationException("Production requires an absolute ApiSecurity:DataProtectionKeyPath on persistent storage.");
        }
        if (options.MaxRequestBodyBytes is < 1 or > 65536 || options.RequestsPerMinute is < 1 or > 1000000
            || options.MutationsPerMinute is < 1 or > 1000000 || options.CreationsPerMinute is < 1 or > 1000000
            || options.GlobalRequestsPerMinute is < 1 or > 1000000 || options.MaxClientBuckets is < 1 or > 65536
            || options.MaxConcurrentRequests is < 1 or > 1000)
        {
            throw new InvalidOperationException("API security limits must be positive and within their supported bounds.");
        }
    }
}
