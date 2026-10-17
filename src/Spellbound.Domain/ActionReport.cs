namespace Spellbound.Domain;

/// <summary>
/// Outcome of a player action as short, human-readable lines (shown as-is in the UI and the news log).
/// A failed action has changed nothing.
/// </summary>
public sealed record ActionReport(bool Succeeded, int TurnsSpent, IReadOnlyList<string> Lines)
{
    public static ActionReport Fail(string reason) => new(false, 0, [reason]);
}
