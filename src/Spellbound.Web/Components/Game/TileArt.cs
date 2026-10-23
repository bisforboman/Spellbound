using Spellbound.Domain;

namespace Spellbound.Web.Components.Game;

/// <summary>
/// The painted tile images (docs/tile-art.md), if they are there: <c>wwwroot/tiles/steady</c>, <c>shifting</c> and
/// <c>wild</c>, as .webp, .png or .svg. The map uses them only when all three exist, so it never mixes art and plain
/// tiles; until then it draws plain tiles with the types' symbols. Looked up once, at startup.
/// </summary>
public sealed class TileArt
{
    private static readonly string[] Extensions = [".webp", ".png", ".svg"];

    public TileArt(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var found = Enum.GetValues<TileType>().ToDictionary(t => t, t => Extensions
            .Select(e => $"tiles/{t.ToString().ToLowerInvariant()}{e}")
            .FirstOrDefault(path => environment.WebRootFileProvider.GetFileInfo(path).Exists));
        Paths = found.Values.All(p => p is not null) ? found.ToDictionary(p => p.Key, p => p.Value!) : null;
    }

    /// <summary>Each type's image, relative to the web root, or null while any is missing.</summary>
    public IReadOnlyDictionary<TileType, string>? Paths { get; }
}
