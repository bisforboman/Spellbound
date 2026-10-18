using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Spellbound.Domain;

namespace Spellbound.Web.Components.Game;

/// <summary>Base for pages that need the signed-in player's mage. Sends players without a mage to /create.</summary>
public abstract class GamePage : ComponentBase
{
    [CascadingParameter]
    private Task<AuthenticationState> AuthState { get; set; } = default!;

    [Inject]
    protected GameService Game { get; set; } = default!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    protected string OwnerId { get; private set; } = string.Empty;

    protected Mage? Mage { get; private set; }

    /// <summary>The round being played, or null before the first one starts.</summary>
    protected Round? Round { get; private set; }

    protected ActionReport? Report { get; private set; }

    protected bool Busy { get; private set; }

    /// <summary>The round's rules. Set with <see cref="Round"/>; only read once there is a mage, so a round.</summary>
    protected GameRules Rules { get; private set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        OwnerId = (await AuthState).User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        await LoadAsync();
        if (Mage is null)
        {
            Navigation.NavigateTo("create");
        }
    }

    protected virtual async Task LoadAsync()
    {
        var realm = await Game.GetRealmAsync();
        (Round, Rules) = (realm?.Round, realm?.Rules!);
        Mage = await Game.GetMageAsync(OwnerId);
    }

    /// <summary>Runs one game action, shows its report and reloads the mage. Ignores taps while busy.</summary>
    protected async Task RunAsync(Func<Task<ActionReport>> action)
    {
        if (Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            Report = await action();
            await LoadAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    protected static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    protected static string Turns(int count) => count == 1 ? "1 turn" : $"{N(count)} turns";
}
