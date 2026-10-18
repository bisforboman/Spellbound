using System.Globalization;
using System.Text.Json;
using Spellbound.Domain;
using Spellbound.Web.Components.Game;

namespace Spellbound.Web.Tests;

/// <summary>
/// The sigils match the theme proposal's JavaScript byte for byte. Fixtures/sigils.json was made by running that
/// script (its school keys are the proposal's old names: radiant is Dawn, verdant Grove, tidal Veil, umbral Umbra).
/// </summary>
public sealed class SigilTests
{
    private static readonly Dictionary<string, MagicSchool> Schools = new()
    {
        ["radiant"] = MagicSchool.Dawn,
        ["verdant"] = MagicSchool.Grove,
        ["ember"] = MagicSchool.Ember,
        ["umbral"] = MagicSchool.Umbra,
        ["tidal"] = MagicSchool.Veil,
    };

    public static TheoryData<string, string, string> Fixtures()
    {
        var data = new TheoryData<string, string, string>();
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sigils.json"));
        foreach (var (key, svg) in JsonSerializer.Deserialize<Dictionary<string, string>>(json)!)
        {
            var parts = key.Split('|');
            data.Add(parts[0], parts[1], svg);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void A_sigil_matches_the_proposals_script(string name, string school, string expected)
    {
        Assert.Equal(expected, SigilGenerator.Svg(name, Schools[school]));
    }

    [Fact]
    public void A_sigil_does_not_depend_on_the_servers_culture()
    {
        var before = CultureInfo.CurrentCulture;
        var invariant = SigilGenerator.Svg("Merlin", MagicSchool.Grove);
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(invariant, SigilGenerator.Svg("Merlin", MagicSchool.Grove));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Names_that_differ_only_in_case_and_spaces_share_a_sigil()
    {
        Assert.Equal(SigilGenerator.Svg("Merlin", MagicSchool.Grove), SigilGenerator.Svg("  MERLIN ", MagicSchool.Grove));
        Assert.NotEqual(SigilGenerator.Svg("Merlin", MagicSchool.Grove), SigilGenerator.Svg("Morgana", MagicSchool.Grove));
    }
}
