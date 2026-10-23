using Spellbound.Web;
using Spellbound.Web.Components;
using Spellbound.Web.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The reusable host (Hosting/): accounts, database, rounds, admin, backups and abuse protection.
builder.AddGameHost();

// The game behind the host's hooks, and its matches (stored in the host's games table; docs/architecture.md).
builder.Services.AddSingleton<SpellboundGame>();
builder.Services.AddSingleton<IGameSchema>(sp => sp.GetRequiredService<SpellboundGame>());
builder.Services.AddSingleton<IGameLifecycle>(sp => sp.GetRequiredService<SpellboundGame>());
builder.Services.AddSingleton<IPlayerSummaries>(sp => sp.GetRequiredService<SpellboundGame>());
builder.Services.AddSingleton<IAccountLifecycle>(sp => sp.GetRequiredService<SpellboundGame>());
builder.Services.AddSingleton(Spellbound.Web.Data.RulesSetup.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<Spellbound.Web.Data.MatchStore>();
builder.Services.AddSingleton<Spellbound.Web.Data.MatchPlay>();
builder.Services.AddSingleton<Spellbound.Web.Data.MatchLobbies>();
builder.Services.AddSingleton<Spellbound.Web.Components.Game.TileArt>();

var app = builder.Build();

await app.UseGameHostAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapGameHost();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program; // for WebApplicationFactory in tests
