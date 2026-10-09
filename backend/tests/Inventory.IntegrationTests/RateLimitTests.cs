using System.Net;
using System.Net.Http.Json;

namespace Inventory.IntegrationTests;

public sealed class LowRateLimitApiFactory : ApiFactory
{
    protected override IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?> { ["RateLimiting:LoginPerMinute"] = "3" };
}

public sealed class RateLimitTests(LowRateLimitApiFactory api) : IClassFixture<LowRateLimitApiFactory>
{
    [Fact]
    public async Task Login_is_rate_limited_per_client()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = api.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var r = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "x@test.local", password = "wrong", client = "web" }, ct);
            statuses.Add(r.StatusCode);
        }

        statuses.Take(3).ShouldAllBe(s => s == HttpStatusCode.Unauthorized);
        statuses[3].ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
