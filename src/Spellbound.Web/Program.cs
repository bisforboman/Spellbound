using Spellbound.Domain;
using Spellbound.Web;
using Spellbound.Web.Components;
using Spellbound.Web.Components.Game;
using Spellbound.Web.Data;
using Spellbound.Web.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The reusable host (Hosting/): accounts, database, rounds, admin, backups and abuse protection.
builder.AddGameHost();

// Spellbound itself, and its answers to the host's questions.
builder.Services.AddSingleton(builder.Configuration.GetSection("GameRules").Get<GameRules>() ?? GameRules.Default);
builder.Services.AddSingleton<IRandomSource, SharedRandom>();
builder.Services.AddSingleton<GameService>();
builder.Services.AddSingleton<IGameSchema, SpellboundSchema>();
builder.Services.AddSingleton<SpellboundHost>();
builder.Services.AddSingleton<IRoundLifecycle>(sp => sp.GetRequiredService<SpellboundHost>());
builder.Services.AddSingleton<IPlayerSummaries>(sp => sp.GetRequiredService<SpellboundHost>());
builder.Services.AddSingleton<IAccountLifecycle>(sp => sp.GetRequiredService<SpellboundHost>());
builder.Services.Configure<AdminPageOptions>(options =>
{
    options.GameSections.Add(typeof(ActionLogAdmin));
    options.GameSections.Add(typeof(GameRulesAdmin));
});

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
