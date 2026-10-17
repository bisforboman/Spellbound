namespace Spellbound.Web.Hosting;

/// <summary>A network (keyed hash of an IP address) an account signed in from, with when it was last seen.</summary>
public sealed class AccountNetwork
{
    public string OwnerId { get; set; } = string.Empty;

    public string Network { get; set; } = string.Empty;

    public DateTimeOffset LastSeen { get; set; }
}

/// <summary>A per-server secret that must survive restarts, created on first use.</summary>
public sealed class ServerSecret
{
    public string Name { get; set; } = string.Empty;

    public byte[] Value { get; set; } = [];
}
