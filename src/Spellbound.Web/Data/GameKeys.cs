using Raven.Client.Documents.Session;
using Spellbound.Domain;

namespace Spellbound.Web.Data;

/// <summary>Keys of Spellbound's own documents. Mages and news are per round, so a new round starts everyone over.</summary>
public static class GameKeys
{
    public const string HallOfFamePrefix = "halloffame/";

    /// <summary>One mage per player per round: the round and owner id are the key, so a second mage can't be stored.</summary>
    public static string MageKey(int round, string ownerId) => $"{MagePrefix(round)}{ownerId}";

    public static string MagePrefix(int round) => $"mages/{round}/";

    public static string NewsKey(int round, string ownerId) => $"news/{round}/{ownerId}";

    public static string HallOfFameKey(int round) => $"{HallOfFamePrefix}{round}";

    /// <summary>Every mage of a round, read by key prefix: consistent and needs no index.</summary>
    public static async Task<IReadOnlyList<Mage>> LoadRoundMagesAsync(IAsyncDocumentSession session, int round)
    {
        var mages = new List<Mage>();
        await using var stream = await session.Advanced.StreamAsync<Mage>(startsWith: MagePrefix(round));
        while (await stream.MoveNextAsync())
        {
            mages.Add(stream.Current.Document);
        }

        return mages;
    }
}
