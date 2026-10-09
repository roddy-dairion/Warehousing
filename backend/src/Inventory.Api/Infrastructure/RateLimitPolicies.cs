using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Api.Infrastructure;

internal sealed class RateLimitOptions
{
    public const string Section = "RateLimiting";

    /// <summary>Login attempts per client IP per minute.</summary>
    public int LoginPerMinute { get; set; } = 10;

    /// <summary>Refresh calls per client IP per minute.</summary>
    public int RefreshPerMinute { get; set; } = 60;
}

internal static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Refresh = "refresh";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(Login, ctx => PerIp(ctx, options.LoginPerMinute));
            limiter.AddPolicy(Refresh, ctx => PerIp(ctx, options.RefreshPerMinute));
            limiter.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = "Too many attempts. Wait a minute and try again.",
                    Extensions = { ["code"] = "rate_limited" },
                }, options: null, contentType: "application/problem+json", cancellationToken: token);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> PerIp(HttpContext context, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
