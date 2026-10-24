using Spellbound.Domain;
using Spellbound.Web.Data;

namespace Spellbound.Web.Tests;

/// <summary>Getting a match through its opening in tests: whoever's turn it is picks the first tile left, until the
/// rounds begin (the bots pick theirs as they come up).</summary>
internal static class Opening
{
    public static async Task PickAsync(MatchPlay play, long id, params string[] owners)
    {
        for (var step = 0; step < 100; step++)
        {
            var view = (await play.ViewAsync(id, owners[0]))!;
            if (view.Match.Phase != MatchPhase.Opening)
            {
                return;
            }

            var owner = view.Owners[view.Match.Picker!.Value] ?? throw new InvalidOperationException("A bot's turn waits on nobody.");
            Assert.True((await play.PickAsync(id, owner, view.Match.ToPick[0])).Succeeded);
        }

        throw new InvalidOperationException("The opening didn't end.");
    }
}
