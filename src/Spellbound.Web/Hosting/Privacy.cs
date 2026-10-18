namespace Spellbound.Web.Hosting;

/// <summary>
/// Who runs this server, for the privacy notice (config section "Privacy"). Kept out of the repository, which is public.
/// Outside Development the app won't start without them: a notice with blanks is worse than none.
/// </summary>
public sealed class PrivacyOptions
{
    /// <summary>Who runs the game and decides what happens to the data, e.g. a name.</summary>
    public string Operator { get; set; } = string.Empty;

    /// <summary>How to reach them about privacy, e.g. an email address.</summary>
    public string Contact { get; set; } = string.Empty;

    /// <summary>Where the server is, e.g. "a rented server in Germany (Hetzner)".</summary>
    public string Hosting { get; set; } = string.Empty;

    /// <summary>Optional: copies of the backups kept elsewhere, and for how long, e.g. "on a storage box for 30 days".</summary>
    public string? OffsiteBackups { get; set; }

    public static void Add(WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("Privacy");
        builder.Services.Configure<PrivacyOptions>(section);
        var options = section.Get<PrivacyOptions>() ?? new PrivacyOptions();
        if (!builder.Environment.IsDevelopment()
            && (string.IsNullOrWhiteSpace(options.Operator) || string.IsNullOrWhiteSpace(options.Contact) || string.IsNullOrWhiteSpace(options.Hosting)))
        {
            throw new InvalidOperationException(
                "The privacy notice needs Privacy__Operator, Privacy__Contact and Privacy__Hosting (see docs/deploy.md).");
        }
    }
}
