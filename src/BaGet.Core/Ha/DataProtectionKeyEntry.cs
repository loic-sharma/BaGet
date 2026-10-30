namespace BaGet.Core;

/// <summary>
/// One key of the ASP.NET Core data protection key ring, kept in the database in cluster mode (<c>Ha:Enabled</c>) so every node
/// signs and reads the same cookies, antiforgery tokens, API-key-free tokens and protected remote credentials. The XML is what the
/// key manager wrote (the key material is encrypted by its own key-encryption setting, or stored as is when none is configured: protect the database).
/// Without HA the host's own key ring (a folder) is used and this table stays empty.
/// </summary>
public class DataProtectionKeyEntry
{
    /// <summary>The longest friendly name stored.</summary>
    public const int MaxNameLength = 200;

    /// <summary>The row's id.</summary>
    public int Key { get; set; }

    /// <summary>The key manager's name for the element (<c>key-{guid}</c>).</summary>
    public string? FriendlyName { get; set; }

    /// <summary>The key's XML element.</summary>
    public string? Xml { get; set; }
}
