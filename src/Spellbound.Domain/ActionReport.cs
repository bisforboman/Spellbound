namespace Spellbound.Domain;

/// <summary>
/// Outcome of a player action as short, human-readable lines (shown as-is in the UI and the news log).
/// A failed action has changed nothing.
/// </summary>
public sealed record ActionReport(bool Succeeded, int TurnsSpent, IReadOnlyList<string> Lines)
{
    /// <summary>A battle's verdict: true if the attacker broke through, false if the defenses held; null for anything
    /// that isn't a battle.</summary>
    public bool? Won { get; init; }

    /// <summary>The lines a player should not miss before confirming, such as units deserting or a spell fading; they
    /// are in <see cref="Lines"/> too.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static ActionReport Fail(string reason) => new(false, 0, [reason]);
}
