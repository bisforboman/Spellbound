using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Spellbound.Web.Hosting;

/// <summary>
/// The database (PostgreSQL through EF Core): accounts, the round pointer, bans and network signals, plus whatever
/// tables the game adds through <see cref="IGameModel"/>. One context, so the host and the game can save in one
/// transaction (a round reset, an account deletion).
/// </summary>
public sealed class HostDb(DbContextOptions<HostDb> options, IEnumerable<IGameModel> games) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<CurrentRound> CurrentRound => Set<CurrentRound>();

    public DbSet<Ban> Bans => Set<Ban>();

    public DbSet<AccountNetwork> AccountNetworks => Set<AccountNetwork>();

    public DbSet<ServerSecret> ServerSecrets => Set<ServerSecret>();

    /// <summary>The round being played, or null before the first one has started.</summary>
    public async Task<CurrentRound?> LoadCurrentRoundAsync(CancellationToken cancellationToken = default) =>
        await CurrentRound.SingleOrDefaultAsync(r => r.Id == Hosting.CurrentRound.OnlyId, cancellationToken);

    public async Task<BanList> LoadBansAsync(CancellationToken cancellationToken = default) =>
        new(await Bans.AsNoTracking().ToDictionaryAsync(b => b.OwnerId, cancellationToken));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Account>(account =>
        {
            account.ToTable("accounts");
            account.HasIndex(a => new { a.Provider, a.Subject }).IsUnique();
            account.HasIndex(a => a.Email);
        });
        builder.Entity<CurrentRound>(round =>
        {
            round.ToTable("current_round");
            round.Property(r => r.Id).ValueGeneratedNever();
            round.Property(r => r.Version).IsRowVersion(); // xmin: two hosts can't both move the round
            round.Ignore(r => r.Round);
        });
        builder.Entity<Ban>(ban =>
        {
            ban.ToTable("bans");
            ban.HasKey(b => b.OwnerId);
        });
        builder.Entity<AccountNetwork>(network =>
        {
            network.ToTable("account_networks");
            network.HasKey(n => new { n.OwnerId, n.Network });
            network.HasIndex(n => n.LastSeen);
        });
        builder.Entity<ServerSecret>(secret =>
        {
            secret.ToTable("server_secrets");
            secret.HasKey(s => s.Name);
        });

        foreach (var game in games)
        {
            game.Configure(builder);
        }
    }

    /// <summary>PostgreSQL's timestamptz only takes UTC, so every DateTimeOffset is stored as UTC, whatever its offset.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();

    private sealed class UtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
        value => value.ToUniversalTime(), value => value);
}
