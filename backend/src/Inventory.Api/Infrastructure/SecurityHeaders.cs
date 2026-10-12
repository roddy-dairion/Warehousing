namespace Inventory.Api.Infrastructure;

/// <summary>
/// Defensive headers on every API response: no MIME sniffing, no framing, no referrer, no caching of API data on shared
/// devices, and a Content-Security-Policy that lets nothing load (the API serves JSON, files and PDFs, never pages).
/// Swagger UI (development only) keeps its own scripts.
/// </summary>
internal static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            if (!context.Request.Path.StartsWithSegments("/swagger"))
            {
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            }
            if (context.Request.Path.StartsWithSegments("/api") && !headers.ContainsKey("Cache-Control"))
            {
                headers.CacheControl = "no-store";
            }
            return Task.CompletedTask;
        });
        await next();
    });
}
