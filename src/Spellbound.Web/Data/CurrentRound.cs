using Spellbound.Domain;

namespace Spellbound.Web.Data;

/// <summary>The one document that says which round is being played. Only <see cref="RoundKeeper"/> writes it.</summary>
public sealed class CurrentRound
{
    public Round Round { get; set; } = default!;
}
