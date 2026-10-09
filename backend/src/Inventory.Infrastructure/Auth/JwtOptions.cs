using System.ComponentModel.DataAnnotations;

namespace Inventory.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required]
    public string Issuer { get; set; } = "inventory-api";

    [Required]
    public string Audience { get; set; } = "inventory-clients";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Supply via user-secrets or the <c>Jwt__SigningKey</c> environment variable.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>Failed attempts before the account is locked, and for how long.</summary>
    [Range(1, 50)]
    public int MaxFailedAttempts { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;
}
