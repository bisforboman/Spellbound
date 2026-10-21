using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Spellbound.Domain;

namespace Spellbound.Web.Data;

/// <summary>What a match keeps in its games row: the seed its randomness comes from, the match itself, and the lines of
/// the last action's report, so the match page can show what just happened.</summary>
public sealed record StoredMatch(int Seed, MatchState Match, IReadOnlyList<string>? LastReport = null, IReadOnlyList<LineTag?>? LastReportTags = null);

/// <summary>
/// JSON for a match's state and rules in the games row. The domain's <see cref="MatchState"/> and
/// <see cref="GameRules"/> are plain records, so this only fixes the format: camelCase names, enums by name, and a hex
/// as a [q, r] pair.
/// </summary>
public static class MatchJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"Stored {typeof(T).Name} was null.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new HexConverter());
        options.MakeReadOnly();
        return options;
    }

    private sealed class HexConverter : JsonConverter<Hex>
    {
        public override Hex Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var pair = JsonSerializer.Deserialize<int[]>(ref reader, options);
            return pair is [var q, var r] ? new Hex(q, r) : throw new JsonException("A hex is stored as [q, r].");
        }

        public override void Write(Utf8JsonWriter writer, Hex value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.Q);
            writer.WriteNumberValue(value.R);
            writer.WriteEndArray();
        }
    }
}
