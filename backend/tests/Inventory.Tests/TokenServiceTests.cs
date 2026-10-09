using Inventory.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Inventory.Tests;

public sealed class TokenServiceTests
{
    private static readonly JwtOptions Options = new() { SigningKey = new string('k', 40), AccessTokenMinutes = 15 };

    [Fact]
    public async Task Access_token_carries_identity_permissions_and_expiry()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
        var service = new TokenService(Microsoft.Extensions.Options.Options.Create(Options), clock);
        var userId = Guid.CreateVersion7();

        var (token, expires) = service.CreateAccessToken(userId, "op@test.local", "Operator", ["WH_OPERATOR"], ["inventory.read", "dispatches.pick"]);

        expires.ShouldBe(clock.GetUtcNow().AddMinutes(15));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            IssuerSigningKey = TokenService.CreateKey(Options.SigningKey),
            ValidateLifetime = false,
        });
        result.IsValid.ShouldBeTrue();
        var jwt = (JsonWebToken)result.SecurityToken;
        jwt.Subject.ShouldBe(userId.ToString());
        jwt.Claims.Where(c => c.Type == CustomClaims.Permission).Select(c => c.Value).ShouldBe(["inventory.read", "dispatches.pick"], ignoreOrder: true);
        jwt.Alg.ShouldBe(SecurityAlgorithms.HmacSha256);
    }

    [Fact]
    public void Refresh_tokens_are_random_and_256_bit()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => TokenService.GenerateRefreshToken()).ToList();
        tokens.Distinct().Count().ShouldBe(100);
        Base64UrlEncoder.DecodeBytes(tokens[0]).Length.ShouldBe(32);
    }

    [Fact]
    public void Hash_is_stable_hex_sha256_and_differs_from_token()
    {
        var token = TokenService.GenerateRefreshToken();
        var hash = TokenService.Hash(token);
        hash.ShouldBe(TokenService.Hash(token));
        hash.Length.ShouldBe(64);
        hash.ShouldNotContain(token);
    }
}
