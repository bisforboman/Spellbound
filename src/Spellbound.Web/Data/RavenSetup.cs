using System.Reflection;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Session;
using Spellbound.Domain;

namespace Spellbound.Web.Data;

public static class RavenSetup
{
    public static DocumentConventions Conventions() => new() { FindIdentityProperty = IsDocumentKey };

    /// <summary>
    /// Only string <c>Id</c> properties are document keys. Mage keeps its Guid Id as plain data
    /// and is stored under <see cref="MageKey"/>.
    /// </summary>
    public static bool IsDocumentKey(MemberInfo member) =>
        member is PropertyInfo { Name: "Id" } property && property.PropertyType == typeof(string);

    /// <summary>Points at the round being played; see <see cref="CurrentRound"/>.</summary>
    public const string CurrentRoundKey = "rounds/current";

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
