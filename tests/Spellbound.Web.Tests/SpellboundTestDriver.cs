using Raven.Client.Documents;
using Raven.Embedded;
using Raven.TestDriver;
using Spellbound.Web.Data;

namespace Spellbound.Web.Tests;

/// <summary>
/// Base for tests that need RavenDB. The in-memory test server is shared by every test class and can only be configured
/// before the first store is created, so it is configured here, once, rather than in each class.
/// </summary>
public abstract class SpellboundTestDriver : RavenTestDriver
{
    static SpellboundTestDriver()
    {
        // The test server is embedded and in-memory; like the app's embedded server, run it without a license file.
        ConfigureServer(new TestServerOptions
        {
            Licensing = new ServerOptions.LicensingOptions { ThrowOnInvalidOrMissingLicense = false },
        });
    }

    protected override void PreInitialize(IDocumentStore documentStore) =>
        documentStore.Conventions.FindIdentityProperty = RavenSetup.IsDocumentKey;
}
