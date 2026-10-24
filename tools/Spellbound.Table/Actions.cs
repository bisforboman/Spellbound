using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spellbound.Domain;

namespace Spellbound.Table;

/// <summary>An AI seat's action, read from JSON: a trait pick, a tile pick in the draft, or a round's orders.</summary>
internal abstract record Action
{
    public sealed record PickTrait(Trait Trait) : Action;

    public sealed record PickTile(Pick Pick) : Action;

    public sealed record GiveOrders(Orders Orders) : Action;

    /// <summary>Reads an action, or says what's wrong with it in one line.</summary>
    public static (Action? Action, string? Problem) Parse(string json, int seat)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, "Send one JSON object.");
            }

            var kinds = new[] { "trait", "pick", "stance" }.Count(k => Get(root, k) is not null);
            if (kinds > 1)
            {
                return (null, "One action at a time: a trait, a pick or a stance.");
            }

            if (Get(root, "trait") is { } trait)
            {
                return Enum.TryParse<Trait>(trait.GetString(), ignoreCase: true, out var t)
                    ? (new PickTrait(t), null)
                    : (null, $"There is no trait \"{trait.GetString()}\".");
            }

            if (Get(root, "pick") is { } pick)
            {
                return (new PickTile(new Pick(seat, HexOf(pick))), null);
            }

            if (Get(root, "stance") is { } stance)
            {
                return (new GiveOrders(OrdersOf(root, stance.GetString() ?? "", seat)), null);
            }

            return (null, "Send {\"trait\": …}, {\"pick\": \"q,r\"} or {\"stance\": …}.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            return (null, ex.Message);
        }
    }

    private static Orders OrdersOf(JsonElement root, string stance, int seat)
    {
        var moves = Get(root, "moves") is { } m ? m.EnumerateArray().Select(MoveOf).ToList() : [];
        var orders = stance.ToLowerInvariant() switch
        {
            "hold" => Orders.Hold(seat, moves),
            "attune" => Orders.Attune(seat, HexOf(Need(root, "target")), moves),
            "summon" => Orders.Summon(seat, HexOf(Need(root, "target")),
                Enum.Parse<UnitKind>(Need(root, "kind").GetString()!, ignoreCase: true), Int(root, "count"), moves),
            "build" => Orders.Build(seat, HexOf(Need(root, "target")),
                Enum.Parse<Upgrade>(Need(root, "upgrade").GetString()!, ignoreCase: true), moves),
            "attack" => Orders.Attack(seat, HexOf(Need(root, "target")),
                [.. Need(root, "send").EnumerateArray().Select(s => new Move(HexOf(Need(s, "from")), HexOf(Need(root, "target")), ArmyOf(s)))],
                moves),
            _ => throw new FormatException($"There is no stance \"{stance}\": attack, attune, summon, build or hold."),
        };

        if (Get(root, "reinforce") is { ValueKind: JsonValueKind.Object } reinforce)
        {
            orders = orders.Reinforcing(HexOf(Need(reinforce, "target")),
                Enum.Parse<UnitKind>(Get(reinforce, "kind")?.GetString() ?? "imp", ignoreCase: true), Int(reinforce, "count"));
        }

        if (Get(root, "spell") is not { ValueKind: JsonValueKind.Object } spell)
        {
            return orders;
        }

        var name = Enum.Parse<Spell>(Need(spell, "name").GetString()!, ignoreCase: true);
        var target = HexOf(Need(spell, "target"));
        return name == Spell.Leyline
            ? orders.CastingLeyline(new Move(target, HexOf(Need(spell, "to")), ArmyOf(spell)))
            : orders.Casting(name, target, Int(spell, "count"));
    }

    private static Move MoveOf(JsonElement e) => new(HexOf(Need(e, "from")), HexOf(Need(e, "to")), ArmyOf(e));

    private static Army ArmyOf(JsonElement e) => new(Int(e, "imps"), Int(e, "golems"));

    private static int Int(JsonElement e, string name) => Get(e, name) is { } v
        ? v.ValueKind == JsonValueKind.String ? int.Parse(v.GetString()!, CultureInfo.InvariantCulture) : v.GetInt32()
        : 0;

    /// <summary>A hex written "q,r" or "(q, r)".</summary>
    public static Hex HexOf(JsonElement e) => HexOf(e.GetString() ?? "");

    public static Hex HexOf(string text) =>
        Regex.Match(text, @"^\s*\(?\s*(-?\d+)\s*,\s*(-?\d+)\s*\)?\s*$") is { Success: true } m
            ? new Hex(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
            : throw new FormatException($"\"{text}\" isn't a tile: write it \"q,r\", like \"1,-2\".");

    private static JsonElement Need(JsonElement e, string name) =>
        Get(e, name) ?? throw new FormatException($"Missing \"{name}\".");

    private static JsonElement? Get(JsonElement e, string name) =>
        e.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: not JsonValueKind.Undefined } p
            ? p.Value
            : null;
}
