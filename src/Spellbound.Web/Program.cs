using Spellbound.Web;
using Spellbound.Web.Components;
using Spellbound.Web.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The reusable host (Hosting/): accounts, database, rounds, admin, backups and abuse protection.
builder.AddGameHost();

// The game's place while it is designed again: it answers the host's questions and keeps a round going.
builder.Services.AddSingleton<PlaceholderGame>();
builder.Services.AddSingleton<IGameSchema>(sp => sp.GetRequiredService<PlaceholderGame>());
builder.Services.AddSingleton<IRoundLifecycle>(sp => sp.GetRequiredService<PlaceholderGame>());
builder.Services.AddSingleton<IPlayerSummaries>(sp => sp.GetRequiredService<PlaceholderGame>());
builder.Services.AddSingleton<IAccountLifecycle>(sp => sp.GetRequiredService<PlaceholderGame>());

// Matches, stored in the host's games table (docs/architecture.md).
builder.Services.AddSingleton<Spellbound.Web.Data.MatchStore>();

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
