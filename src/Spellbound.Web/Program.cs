using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Raven.Client.Documents;
using Raven.Embedded;
using Raven.Identity;
using Spellbound.Domain;
using Spellbound.Web;
using Spellbound.Web.Components;
using Spellbound.Web.Components.Account;
using Spellbound.Web.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// Embedded RavenDB, started on first use so tests can swap in their own store.
builder.Services.AddSingleton<IDocumentStore>(_ =>
{
    var settings = builder.Configuration.GetSection("RavenDb");
    EmbeddedServer.Instance.StartServer(new ServerOptions
    {
        DataDirectory = Path.Combine(builder.Environment.ContentRootPath, settings["DataDirectory"] ?? "RavenData"),
    });
    return EmbeddedServer.Instance.GetDocumentStore(new DatabaseOptions(settings["Database"] ?? "Spellbound")
    {
        Conventions = RavenSetup.Conventions(),
    });
});
builder.Services.AddScoped(sp => sp.GetRequiredService<IDocumentStore>().OpenAsyncSession()); // used by RavenDB.Identity

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRavenDbIdentityStores<ApplicationUser>(_ => { })
    .AddUserManager<SavingUserManager>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.AddSingleton(builder.Configuration.GetSection("GameRules").Get<GameRules>() ?? GameRules.Default);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GameService>();

var app = builder.Build();

_ = app.Services.GetRequiredService<IDocumentStore>(); // start embedded RavenDB now, not on the first request

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();

public partial class Program; // for WebApplicationFactory in tests
