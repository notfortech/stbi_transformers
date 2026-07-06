using Microsoft.Extensions.Options;

namespace DashboardAgents.Api;

public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<KoruAuthOptions> options)
{
    private const string HeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        // Allow Swagger UI through in Development without a key
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await next(context);
            return;
        }

        var expectedKey = options.Value.ApiKey;

        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            // No key configured — fail closed to avoid accidentally running open
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync("API key is not configured on the server.");
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var providedKey)
            || !string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: missing or invalid API key.");
            return;
        }

        await next(context);
    }
}
