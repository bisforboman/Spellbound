namespace Spellbound.Web.Hosting;

/// <summary>The networks (keyed hashes of IP addresses) an account signed in from, with when each was last seen.</summary>
public sealed class AccountSignal
{
    public string OwnerId { get; set; } = string.Empty;

    public Dictionary<string, DateTimeOffset> Networks { get; set; } = [];
}

/// <summary>Per-server secrets that must survive restarts, created on first use.</summary>
public sealed class ServerSecrets
{
    public byte[] SignalKey { get; set; } = [];
}
