using System.Text.Json;
using System.Text.Json.Serialization;
using Spellbound.Domain;

namespace Spellbound.Table;

/// <summary>
/// One match on disk, shared by the AI seats that play it: the match, the seats played by AI (the rest are the
/// scripted bots), and what each AI seat has committed for the current step. A step (an opening pick or a round)
/// resolves once every AI seat has acted; its randomness comes from the seed and the step, so a match replays.
/// </summary>
internal sealed record TableState(
    int Seed, int Step, int[] AiSeats, MatchState Match, Dictionary<int, string> Pending, string[] LastReport,
    LineTag?[]? LastReportTags = null)
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public Match Load() => Domain.Match.Load(Match, GameRules.Default);

    /// <summary>The lines of the last step's report that a seat may read: the fog of war hides the rest.</summary>
    public IReadOnlyList<string> LastReportFor(Match match, int seat) =>
        match.ReportFor(new Report(true, LastReport, LastReportTags), seat).Lines;

    public IRandomSource StepRandom() => new SeededRandom(unchecked((Seed * 1000) + Step));
}

/// <summary>Reads and writes a table file under an exclusive lock, so two AI seats acting at once take turns.</summary>
internal static class TableFile
{
    public static TableState Read(string path) =>
        JsonSerializer.Deserialize<TableState>(File.ReadAllText(path), TableState.Json)
        ?? throw new InvalidDataException($"{path} isn't a table file.");

    /// <summary>Runs a change to the table with the file locked; the change returns the new state (or the same one).</summary>
    public static T Update<T>(string path, Func<TableState, (TableState State, T Result)> change)
    {
        using var lockFile = Lock(path + ".lock");
        var (state, result) = change(Read(path));
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, TableState.Json));
        File.Move(temp, path, overwrite: true);
        return result;
    }

    public static void Create(string path, TableState state) =>
        File.WriteAllText(path, JsonSerializer.Serialize(state, TableState.Json));

    /// <summary>Appends one line to the match's log: who did what, and what came of it.</summary>
    public static void Log(string path, object entry) =>
        File.AppendAllText(path + ".log.jsonl", JsonSerializer.Serialize(entry, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }) + "\n");

    private static FileStream Lock(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (attempt < 600)
            {
                Thread.Sleep(100);
            }
        }
    }
}
