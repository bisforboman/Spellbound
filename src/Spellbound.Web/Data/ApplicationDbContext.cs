using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Spellbound.Domain;

namespace Spellbound.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Mage> Mages => Set<Mage>();

    public DbSet<NewsEntry> News => Set<NewsEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Mage>(mage =>
        {
            mage.Property(m => m.Id).ValueGeneratedNever();
            mage.HasIndex(m => m.OwnerId).IsUnique(); // becomes (RoundId, OwnerId) in M3
            mage.Property(m => m.Name).HasMaxLength(Mage.MaxNameLength);
            mage.Property(m => m.School).HasConversion<string>().HasMaxLength(16);

            // SQLite has no rowversion: bumped in SaveChangesAsync for every modified mage.
            mage.Property<long>("Version").IsConcurrencyToken();

            mage.OwnsMany(m => m.ActiveEffects, effect =>
            {
                effect.WithOwner().HasForeignKey("MageId");
                effect.Property<int>("Id");
                effect.HasKey("Id");
                effect.Property(e => e.SpellId).HasMaxLength(64);
            });
            mage.Navigation(m => m.ActiveEffects).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<NewsEntry>(news =>
        {
            news.HasIndex(n => n.MageId);
            news.HasOne<Mage>().WithMany().HasForeignKey(n => n.MageId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Mage>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Property<long>("Version").CurrentValue++;
            }
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
