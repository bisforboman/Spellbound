using Spellbound.Domain;

namespace Spellbound.Web.Hosting;

/// <summary>The one row that says which round is being played. Only <see cref="RoundKeeper"/> writes it.</summary>
public sealed class CurrentRound
{
    public const int OnlyId = 1;

    public int Id { get; set; } = OnlyId;

    public int Number { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    /// <summary>PostgreSQL's row version (xmin), for optimistic concurrency.</summary>
    public uint Version { get; set; }

    public Round Round
    {
        get => new(Number, StartsAt, EndsAt);
        set => (Number, StartsAt, EndsAt) = (value.Number, value.StartsAt, value.EndsAt);
    }
}
