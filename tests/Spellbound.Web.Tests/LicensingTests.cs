using Spellbound.Web.Data;

namespace Spellbound.Web.Tests;

public sealed class LicensingTests
{
    private const string Key = """{"Id":"test","Name":"Spellbound","Keys":[]}""";

    [Fact]
    public void Production_without_a_key_refuses_to_start()
    {
        var error = Assert.Throws<InvalidOperationException>(() => RavenSetup.Licensing(null, required: true));
        Assert.Contains("RavenDb__License", error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => RavenSetup.Licensing("  ", required: true));
    }

    [Fact]
    public void Production_with_a_key_passes_it_on_and_rejects_an_invalid_one()
    {
        var licensing = RavenSetup.Licensing(Key, required: true);

        Assert.Equal(Key, licensing.License);
        Assert.True(licensing.ThrowOnInvalidOrMissingLicense);
    }

    [Fact]
    public void Development_runs_without_a_key()
    {
        var licensing = RavenSetup.Licensing(null, required: false);

        Assert.Null(licensing.License);
        Assert.False(licensing.ThrowOnInvalidOrMissingLicense);
    }
}
