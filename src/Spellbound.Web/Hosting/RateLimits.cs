using System.Threading.RateLimiting;

namespace Spellbound.Web.Hosting;

/// <summary>
/// Per-IP request limits, set in the "RateLimits" config section. Behind Caddy the IP is the player's (see
/// ForwardedHeaders). Households and mobile carriers can share one address, so the defaults are generous.
/// </summary>
public sealed record RateLimits
{
    /// <summary>Sign-up, login and other account form posts, per window.</summary>
    public int AccountPosts { get; init; } = 10;

    public TimeSpan AccountWindow { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Opening the live game connection (each takes a couple of requests), per minute.</summary>
    public int ConnectionRequestsPerMinute { get; init; } = 60;

    /// <summary>Pages and other requests that aren't static files, per minute.</summary>
    public int RequestsPerMinute { get; init; } = 300;

    public const string RejectionMessage = "Too many requests from your network. Please wait a minute and try again.";

    public PartitionedRateLimiter<HttpContext> CreateLimiter() =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var path = context.Request.Path;

            if (HttpMethods.IsPost(context.Request.Method) && path.StartsWithSegments("/Account"))
            {
                return Fixed($"account:{ip}", AccountPosts, AccountWindow);
            }

            if (path.StartsWithSegments("/_blazor"))
            {
                return Fixed($"connect:{ip}", ConnectionRequestsPerMinute, TimeSpan.FromMinutes(1));
            }

            // Static files (anything with an extension, and the framework scripts) are cheap and many per page.
            return Path.HasExtension(path.Value) || path.StartsWithSegments("/_framework")
                ? RateLimitPartition.GetNoLimiter("static")
                : Fixed($"page:{ip}", RequestsPerMinute, TimeSpan.FromMinutes(1));
        });

    private static RateLimitPartition<string> Fixed(string key, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0,
        });
}
