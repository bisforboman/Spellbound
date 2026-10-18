using System.Globalization;
using System.Text;
using Spellbound.Domain;

namespace Spellbound.Web.Components.Game;

/// <summary>
/// Draws a mage's sigil: an SVG brand hashed from their name, with their school's mark in the middle (docs/theme.md).
/// A port of the theme proposal's JavaScript: the same hash (FNV-1a), the same PRNG (mulberry32) and the same draw
/// order, so a name gives the same sigil in both. The name is only hashed, never written into the markup.
/// </summary>
public static class SigilGenerator
{
    private const double C = 50;

    public static string Svg(string name, MagicSchool school)
    {
        ArgumentNullException.ThrowIfNull(name);
        var next = Mulberry32(Fnv1a(name.Trim().ToLowerInvariant()));
        var svg = new StringBuilder(
            """<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" role="img" aria-label="Sigil">""");
        svg.Append(Circle(46, 2));
        if (next() < 0.5)
        {
            svg.Append(Circle(42.5, 0.8));
        }

        var marks = 5 + (int)Math.Floor(next() * 8);
        var offset = next() * 360;
        var markStyle = (int)Math.Floor(next() * 3);
        for (var i = 0; i < marks; i++)
        {
            var a = Rad(offset + (i * 360.0 / marks));
            var style = i % 3 == 0 && next() < 0.5 ? (markStyle + 1) % 3 : markStyle;
            svg.Append(style switch
            {
                0 => Line(Polar(33, a), Polar(39, a), 1.4),
                1 => Dot(Polar(36, a), 1.7),
                _ => Diamond(Polar(36, a), 2.6, a),
            });
        }

        var n = 3 + (int)Math.Floor(next() * 5);
        var star = n % 2 == 1 && n >= 5 && next() < 0.6;
        var rot = next() * 360;
        svg.Append(Figure(n, star ? 2 : 1, 28, rot));
        if (next() < 0.55)
        {
            svg.Append(Circle(16, 0.8));
        }

        return svg.Append(Emblem(school)).Append("</svg>").ToString();
    }

    private static uint Fnv1a(string text)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash = unchecked(hash * 16777619u);
        }

        return hash;
    }

    private static Func<double> Mulberry32(uint seed) => () =>
    {
        unchecked
        {
            seed += 0x6D2B79F5u;
            var t = seed;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + ((t ^ (t >> 7)) * (t | 61));
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    };

    private static string Emblem(MagicSchool school) => school switch
    {
        MagicSchool.Dawn => """<g stroke-width="1.6"><circle cx="50" cy="50" r="5" fill="currentColor" fill-opacity="0.85"/>""" +
            string.Concat(Enumerable.Range(0, 8).Select(i => Line(Polar(7.5, Rad(i * 45)), Polar(i % 2 == 0 ? 11.5 : 10, Rad(i * 45)), 1.6))) +
            "</g>",
        MagicSchool.Grove => """<g stroke-width="1.4"><path d="M50 38.5C58.5 43.5 58.5 56.5 50 61.5C41.5 56.5 41.5 43.5 50 38.5Z" fill="currentColor" fill-opacity="0.85"/><path d="M50 41V60M50 47L54 44M50 52L46 49M50 56L54 53" style="stroke:var(--paper, #f4ecd8)" stroke-width="1"/></g>""",
        MagicSchool.Veil => """<g stroke-width="1.8"><path d="M39 45.5q2.75 -4 5.5 0t5.5 0t5.5 0t5.5 0"/><path d="M39 51.5q2.75 -4 5.5 0t5.5 0t5.5 0t5.5 0"/><path d="M42 57.5q2 -3 4 0t4 0t4 0t4 0" stroke-width="1.3"/></g>""",
        MagicSchool.Ember => """<path d="M50 38C55 45 59 50 57.5 56C56.5 60 53.5 62 50 62C46.5 62 43.5 60 42.5 56C41.5 51 45.5 48 47 43C48.5 47 50.5 49 52 52C52.5 47 51.5 42 50 38Z" fill="currentColor" fill-opacity="0.85" stroke-width="1.3"/>""",
        MagicSchool.Umbra => """<path d="M54 39A11 11 0 1 0 54 61A13 13 0 0 1 54 39Z" fill="currentColor" fill-opacity="0.85" stroke-width="1.3"/>""",
        _ => Dot((C, C), 4),
    };

    private static double Rad(double degrees) => degrees * Math.PI / 180;

    private static (double X, double Y) Polar(double r, double a) => (C + (r * Math.Cos(a)), C + (r * Math.Sin(a)));

    // Rounded to two decimals the way JavaScript's Math.round and String() do it (every number here is positive).
    private static string F(double v) =>
        (Math.Round(v * 100, MidpointRounding.AwayFromZero) / 100).ToString(CultureInfo.InvariantCulture);

    private static string Points(IEnumerable<(double X, double Y)> points) =>
        string.Join(" ", points.Select(p => $"{F(p.X)},{F(p.Y)}"));

    private static string Circle(double r, double w) => $"""<circle cx="50" cy="50" r="{F(r)}" stroke-width="{F(w)}"/>""";

    private static string Line((double X, double Y) a, (double X, double Y) b, double w) =>
        $"""<line x1="{F(a.X)}" y1="{F(a.Y)}" x2="{F(b.X)}" y2="{F(b.Y)}" stroke-width="{F(w)}"/>""";

    private static string Dot((double X, double Y) p, double r) =>
        $"""<circle cx="{F(p.X)}" cy="{F(p.Y)}" r="{F(r)}" fill="currentColor" stroke="none"/>""";

    private static string Diamond((double X, double Y) p, double s, double a)
    {
        double ux = Math.Cos(a), uy = Math.Sin(a), vx = -uy, vy = ux;
        var points = Points([
            (p.X + (ux * s), p.Y + (uy * s)), (p.X + (vx * s * .6), p.Y + (vy * s * .6)),
            (p.X - (ux * s), p.Y - (uy * s)), (p.X - (vx * s * .6), p.Y - (vy * s * .6))]);
        return $"""<polygon points="{points}" stroke-width="1" fill="currentColor" fill-opacity="0.35"/>""";
    }

    private static string Figure(int n, int step, double r, double rot)
    {
        var points = Enumerable.Range(0, n).Select(i => Polar(r, Rad(rot + ((i * step % n) * 360.0 / n) - 90)));
        return $"""<polygon points="{Points(points)}" stroke-width="1.3"/>""";
    }
}
