using System.Reflection;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Session;
using Raven.Embedded;

namespace Spellbound.Web.Hosting;

/// <summary>The hosting side of the database: conventions, licence, and the keys of the documents it owns.</summary>
public static class HostStore
{
    public static DocumentConventions Conventions() => new() { FindIdentityProperty = IsDocumentKey };

    /// <summary>
    /// Only string <c>Id</c> properties are document keys. Other <c>Id</c>s (such as a Guid) stay plain data, and
    /// their documents are stored under keys the owner chooses.
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

    public const string BansKey = "admin/bans";

    public const string SecretsKey = "admin/secrets";

    public const string SignalPrefix = "signals/";

    public static string SignalKey(string ownerId) => $"{SignalPrefix}{ownerId}";

    public static async Task<BanList> LoadBansAsync(IAsyncDocumentSession session) =>
        await session.LoadAsync<BanList>(BansKey) ?? new BanList();
}
