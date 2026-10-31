using System.Buffers;

namespace NumberForge.Api.Security;

public sealed class ApiRequestGuardMiddleware(RequestDelegate next, ApiSecurityOptions options, IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        if (!environment.IsDevelopment() && !request.IsHttps)
        {
            await Results.Problem(statusCode: 400, title: "HTTPS is required.").ExecuteAsync(context);
            return;
        }

        // Browser-generated metadata and an exact origin allowlist supplement the Strict SameSite cookie.
        // Run this before authentication so forbidden cross-site requests cannot create a guest cookie.
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        bool unsafeMethod = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method);
        var origin = request.Headers.Origin;
        bool trustedOrigin = origin.Count == 1 && NormalizeOrigin(origin[0]) is string normalized
            && options.AllowedOrigins.Contains(normalized, StringComparer.OrdinalIgnoreCase);
        if (string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase)
            || (origin.Count > 0 && !trustedOrigin)
            || (unsafeMethod && !trustedOrigin && !string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase)))
        {
            await Results.Problem(statusCode: 403, title: "This request origin is not allowed.").ExecuteAsync(context);
            return;
        }

        if (request.ContentLength > options.MaxRequestBodyBytes)
        {
            await Results.Problem(statusCode: 413, title: "Request body is too large.").ExecuteAsync(context);
            return;
        }
        if (!unsafeMethod)
        {
            await next(context);
            return;
        }

        // Read a small bounded body before model binding. This also catches chunked requests that omit Content-Length.
        var bytes = ArrayPool<byte>.Shared.Rent(options.MaxRequestBodyBytes + 1);
        var originalBody = request.Body;
        try
        {
            int length = 0;
            while (true)
            {
                int read = await originalBody.ReadAsync(bytes.AsMemory(length, options.MaxRequestBodyBytes + 1 - length),
                    context.RequestAborted);
                if (read == 0) break;
                length += read;
                if (length > options.MaxRequestBodyBytes)
                {
                    await Results.Problem(statusCode: 413, title: "Request body is too large.").ExecuteAsync(context);
                    return;
                }
            }
            if (length > 0 && !request.HasJsonContentType())
            {
                await Results.Problem(statusCode: 415, title: "A JSON request body is required.").ExecuteAsync(context);
                return;
            }

            using var buffered = new MemoryStream(bytes, 0, length, writable: false);
            request.Body = buffered;
            await next(context);
        }
        finally
        {
            request.Body = originalBody;
            ArrayPool<byte>.Shared.Return(bytes, clearArray: true);
        }
    }

    internal static string? NormalizeOrigin(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
        {
            return null;
        }
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
