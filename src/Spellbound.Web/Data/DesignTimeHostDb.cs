using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model (the host's tables and Spellbound's) without starting the app,
/// which would connect to a database and migrate it. The connection string is never opened.
/// </summary>
public sealed class DesignTimeHostDb : IDesignTimeDbContextFactory<HostDb>
{
    public HostDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<HostDb>().UseNpgsql("Host=localhost;Database=design_time").Options, [new SpellboundModel()]);
}
