using Spellbound.Domain;

namespace Spellbound.Web.Data;

/// <summary>
/// The rules new matches start under: <see cref="GameRules.Default"/> with any number overridden in the "GameRules"
/// configuration section (appsettings, or environment variables such as GameRules__StartingMana=12). A match keeps the
/// rules it started with (they are stored in its row), so a change only affects matches started after it.
/// </summary>
public static class RulesSetup
{
    public const string Section = "GameRules";

    public static GameRules FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(Section);
        var rules = section.Get<GameRules>() ?? GameRules.Default;

        // A list given in configuration replaces the default one; the binder would otherwise add to it.
        return rules with
        {
            SteadyYields = List(section, nameof(GameRules.SteadyYields)) ?? GameRules.Default.SteadyYields,
            ShiftingYields = List(section, nameof(GameRules.ShiftingYields)) ?? GameRules.Default.ShiftingYields,
            WildYields = List(section, nameof(GameRules.WildYields)) ?? GameRules.Default.WildYields,
            LaterUnveilRounds = List(section, nameof(GameRules.LaterUnveilRounds)) ?? GameRules.Default.LaterUnveilRounds,
        };
    }

    private static int[]? List(IConfigurationSection section, string name) =>
        section.GetSection(name).Exists() ? section.GetSection(name).Get<int[]>() : null;
}
