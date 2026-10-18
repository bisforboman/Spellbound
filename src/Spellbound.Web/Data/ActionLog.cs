using System.Security.Cryptography;
using System.Text;
using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Data;

/// <summary>Passes the real luck through and remembers every number it gave, so an action can be replayed later.</summary>
public sealed class RecordingRandom(IRandomSource inner) : IRandomSource
{
    private readonly List<double> draws = [];

    public IReadOnlyList<double> Draws => draws;

    public double NextDouble()
    {
        var draw = inner.NextDouble();
        draws.Add(draw);
        return draw;
    }
}

/// <summary>A mage's main numbers after an action: what balance questions need, without replaying anything.</summary>
public sealed record MageState(
    int Land, int Nodes, int Towns, int Gold, int Mana, int Turns, int Networth, IReadOnlyDictionary<string, int> Army)
{
    public static MageState Of(Mage mage, GameRules rules) =>
        new(mage.Land, mage.Nodes, mage.Towns, mage.Gold, mage.Mana, mage.Turns, mage.Networth(rules),
            mage.Army.Where(u => u.Value > 0).ToDictionary(u => u.Key, u => u.Value));
}

/// <summary>One logged action, as the admin page shows it.</summary>
/// <param name="Against">Another mage's attack or hex on this one; <paramref name="Lines"/> are then this mage's side.</param>
/// <param name="OtherOwnerId">The other mage's owner in an attack or hex.</param>
public sealed record LoggedAction(
    DateTimeOffset At, string Action, bool Against, string? OtherOwnerId, string Inputs, IReadOnlyList<string> Lines);

/// <summary>
/// The action log (table <c>actions</c>): one row per successful player action, written in the action's own
/// transaction, so it holds exactly what happened. See docs/architecture.md, "Persistence notes".
/// </summary>
public static class ActionLog
{
    /// <summary>A short fingerprint of the rules in force. They can be tuned mid-round; a replay must use the same ones.</summary>
    public static string RulesHash(GameRules rules) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DomainJson.Serialize(rules))))[..16];

    public static Task WriteAsync(
        this DbSession db,
        int round,
        Mage mage,
        DateTimeOffset at,
        string action,
        object inputs,
        IReadOnlyList<double> draws,
        ActionReport report,
        GameRules rules,
        string rulesHash,
        string? targetOwnerId = null,
        IReadOnlyList<string>? targetLines = null) =>
        db.ExecuteAsync(
            """
            INSERT INTO actions (round, owner_id, at, action, target_owner_id, inputs, draws, result, state, rules_hash)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            """,
            round,
            mage.OwnerId,
            at,
            action,
            targetOwnerId,
            new Jsonb(DomainJson.Serialize(inputs)),
            new Jsonb(DomainJson.Serialize(draws)),
            new Jsonb(DomainJson.Serialize(new { lines = report.Lines, targetLines })),
            new Jsonb(DomainJson.Serialize(MageState.Of(mage, rules))),
            rulesHash);

    /// <summary>A player's latest actions in a round, newest first, including attacks and hexes against them.</summary>
    public static Task<List<LoggedAction>> LoadAsync(this DbSession db, int round, string ownerId, int limit) =>
        db.QueryAsync(
            """
            SELECT at, action, owner_id <> $2, CASE WHEN owner_id = $2 THEN target_owner_id ELSE owner_id END, inputs::text,
                   coalesce(nullif(CASE WHEN owner_id = $2 THEN result -> 'lines' ELSE result -> 'targetLines' END, 'null'), '[]')::text
            FROM actions
            WHERE round = $1 AND (owner_id = $2 OR target_owner_id = $2)
            ORDER BY at DESC, id DESC
            LIMIT $3
            """,
            r => new LoggedAction(
                r.GetFieldValue<DateTimeOffset>(0),
                r.GetString(1),
                r.GetBoolean(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4),
                DomainJson.Deserialize<List<string>>(r.GetString(5))),
            round, ownerId, limit);
}
