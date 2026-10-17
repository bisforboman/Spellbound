using Microsoft.EntityFrameworkCore;
using Spellbound.Domain;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Data;

/// <summary>
/// A mage, stored as one row per player per round: the round and owner id are the key, so a second mage can't be
/// stored, and a new round starts everyone over. The mage itself is a jsonb snapshot (<see cref="DomainJson"/>).
/// </summary>
public sealed class MageRecord
{
    public int Round { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The mage's public id (<see cref="Mage.Id"/>), for picking targets. Never changes.</summary>
    public Guid MageId { get; set; }

    public string Data { get; set; } = "{}";

    /// <summary>PostgreSQL's row version (xmin): a double-tap can't spend the same turns twice.</summary>
    public uint Version { get; set; }

    public static MageRecord For(int round, Mage mage) =>
        new() { Round = round, OwnerId = mage.OwnerId, MageId = mage.Id, Data = DomainJson.Serialize(mage) };

    public Mage Load() => DomainJson.Deserialize<Mage>(Data);

    public void Save(Mage mage) => Data = DomainJson.Serialize(mage);
}

/// <summary>A mage's recent action reports, newest first, in one row next to the mage.</summary>
public sealed class NewsRecord
{
    public int Round { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public string Entries { get; set; } = "[]";

    /// <summary>Hostile entries (attacks and hexes) the player hasn't seen on the overview yet.</summary>
    public int Unseen { get; set; }

    public uint Version { get; set; }

    public NewsLog Load() => new() { Entries = DomainJson.Deserialize<List<NewsEntry>>(Entries), Unseen = Unseen };

    public void Save(NewsLog log) => (Entries, Unseen) = (DomainJson.Serialize(log.Entries), log.Unseen);
}

/// <summary>A finished round's hall of fame.</summary>
public sealed class HallOfFameRecord
{
    public int Round { get; set; }

    public string Data { get; set; } = "{}";
}

/// <summary>Spellbound's tables in the host's database (see <see cref="IGameModel"/>).</summary>
public sealed class SpellboundModel : IGameModel
{
    public void Configure(ModelBuilder model)
    {
        model.Entity<MageRecord>(mage =>
        {
            mage.ToTable("mages");
            mage.HasKey(m => new { m.Round, m.OwnerId });
            mage.HasIndex(m => m.MageId).IsUnique();
            mage.HasIndex(m => m.OwnerId); // deleting an account deletes its mages from every round
            mage.Property(m => m.Data).HasColumnType("jsonb");
            mage.Property(m => m.Version).IsRowVersion();
        });
        model.Entity<NewsRecord>(news =>
        {
            news.ToTable("news");
            news.HasKey(n => new { n.Round, n.OwnerId });
            news.HasIndex(n => n.OwnerId);
            news.Property(n => n.Entries).HasColumnType("jsonb");
            news.Property(n => n.Version).IsRowVersion();
        });
        model.Entity<HallOfFameRecord>(fame =>
        {
            fame.ToTable("hall_of_fame");
            fame.HasKey(f => f.Round);
            fame.Property(f => f.Round).ValueGeneratedNever();
            fame.Property(f => f.Data).HasColumnType("jsonb");
        });
    }
}

/// <summary>Reads Spellbound's tables.</summary>
public static class GameData
{
    public static DbSet<MageRecord> Mages(this HostDb db) => db.Set<MageRecord>();

    public static DbSet<NewsRecord> News(this HostDb db) => db.Set<NewsRecord>();

    public static DbSet<HallOfFameRecord> HallsOfFame(this HostDb db) => db.Set<HallOfFameRecord>();

    /// <summary>Every mage of a round, read-only.</summary>
    public static async Task<IReadOnlyList<Mage>> LoadRoundMagesAsync(this HostDb db, int round, CancellationToken cancellationToken = default) =>
        [.. (await db.Mages().AsNoTracking().Where(m => m.Round == round).Select(m => m.Data).ToListAsync(cancellationToken))
            .Select(DomainJson.Deserialize<Mage>)];
}
