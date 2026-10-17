using System.Reflection;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Session;
using Raven.Embedded;
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

    /// <summary>
    /// The embedded server's licence. The key is the licence JSON from RavenDB, set as RavenDb:License
    /// (RavenDb__License) or RAVEN_License. Outside development a key is required, and RavenDB refuses to start with
    /// an invalid one: without a key the server would run under the AGPLv3 instead of the licence's terms.
    /// </summary>
    public static ServerOptions.LicensingOptions Licensing(string? license, bool required)
    {
        if (required && string.IsNullOrWhiteSpace(license))
        {
            throw new InvalidOperationException(
                "No RavenDB licence key is configured. Set RavenDb__License (or RAVEN_License) to the licence JSON.");
        }

        return new ServerOptions.LicensingOptions
        {
            License = string.IsNullOrWhiteSpace(license) ? null : license,
            ThrowOnInvalidOrMissingLicense = required,
        };
    }

    /// <summary>Points at the round being played; see <see cref="CurrentRound"/>.</summary>
    public const string CurrentRoundKey = "rounds/current";

    public const string HallOfFamePrefix = "halloffame/";

    public const string BansKey = "admin/bans";

    public const string SecretsKey = "admin/secrets";

    public const string SignalPrefix = "signals/";

    public static string SignalKey(string ownerId) => $"{SignalPrefix}{ownerId}";

    /// <summary>One mage per player per round: the round and owner id are the key, so a second mage can't be stored.</summary>
    public static string MageKey(int round, string ownerId) => $"{MagePrefix(round)}{ownerId}";

    public static string MagePrefix(int round) => $"mages/{round}/";

    public static string NewsKey(int round, string ownerId) => $"news/{round}/{ownerId}";

    public static string HallOfFameKey(int round) => $"{HallOfFamePrefix}{round}";

    public static async Task<BanList> LoadBansAsync(IAsyncDocumentSession session) =>
        await session.LoadAsync<BanList>(BansKey) ?? new BanList();

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
