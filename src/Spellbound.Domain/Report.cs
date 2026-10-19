namespace Spellbound.Domain;

/// <summary>What an action did, in short lines to show the players. A failed action changed nothing; its lines say
/// why.</summary>
public sealed record Report(bool Succeeded, IReadOnlyList<string> Lines)
{
    public static Report Fail(string reason) => new(false, [reason]);
}
