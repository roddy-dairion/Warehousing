namespace Inventory.Api.Infrastructure;

/// <summary>The web client's refresh token cookie: HttpOnly, Secure, SameSite=Strict, scoped to the auth routes.</summary>
internal static class RefreshCookie
{
    public const string Name = "wh_rt";
    public const string Path = "/api/v1/auth";

    public static void Set(HttpResponse response, string token, DateTimeOffset expires) =>
        response.Cookies.Append(Name, token, Options(expires));

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(DateTimeOffset.UnixEpoch));

    public static string? Read(HttpRequest request) => request.Cookies.TryGetValue(Name, out var v) ? v : null;

    private static CookieOptions Options(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expires,
        IsEssential = true,
    };
}
