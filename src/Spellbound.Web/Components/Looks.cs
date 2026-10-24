namespace Spellbound.Web.Components;

/// <summary>One of the page looks (docs/theme.md): a <c>[data-look]</c> block in app.css.</summary>
public sealed record Look(string Name, string Title, string Line, string Cost);

/// <summary>
/// The page looks a player can swap between on the account page. The choice is a cookie per device, not stored with the
/// account: App.razor reads it and sets <c>data-look</c> on <c>&lt;html&gt;</c>, so the page is drawn in it from the
/// first paint.
/// </summary>
public static class Looks
{
    public const string Cookie = "look";

    public static readonly IReadOnlyList<Look> All =
    [
        new("grimoire", "Burned grimoire", "Soot-black paper, fine grain and a vignette: the first look.", "About 1 KB of CSS"),
        new("sigil", "Sigil night", "Indigo-black, a faint summoning circle behind the board, a soft glow.", "About 1.5 KB of CSS"),
        new("table", "Candlelit table", "A pool of warm light on dark wood, the board on a brass-rimmed felt tray.", "About 1 KB of CSS"),
        new("dust", "Arcane dust", "Star and ember motes over a faint nebula.", "About 1 KB of CSS"),
        new("slate", "Painted rune-slate", "A painted slate table with engraved runes.", "A 34 KB image, once"),
    ];

    public static Look Default => All[0];

    /// <summary>The look this device chose, or the default.</summary>
    public static Look Of(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Find(context.Request.Cookies[Cookie]) ?? Default;
    }

    public static Look? Find(string? name) => All.FirstOrDefault(l => l.Name == name);

    /// <summary><c>/look/{name}?back=/page</c>: keeps the look on this device for a year and opens the page again.</summary>
    public static void MapLooks(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/look/{name}", IResult (HttpContext context, string name, string? back) =>
        {
            if (Find(name) is not { } look)
            {
                return TypedResults.NotFound();
            }

            context.Response.Cookies.Append(Cookie, look.Name, new CookieOptions
            {
                MaxAge = TimeSpan.FromDays(365),
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                HttpOnly = true,
                IsEssential = true,
            });
            return TypedResults.LocalRedirect(IsLocal(back) ? back! : "/Account/Manage");
        });
    }

    private static bool IsLocal(string? url) =>
        url is { Length: > 0 } && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)
        && !url.StartsWith("/\\", StringComparison.Ordinal);
}
