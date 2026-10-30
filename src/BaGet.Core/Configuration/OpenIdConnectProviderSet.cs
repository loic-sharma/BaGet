using System;
using System.Collections.Generic;
using System.Linq;

namespace BaGet.Core;

/// <summary>
/// The providers whose authentication handlers were registered at startup (singleton): what the sign-in screen offers and what the
/// challenge, the callback and the re-authentication accept. Resolved once by <see cref="OpenIdConnectProviders.Resolve"/>.
/// </summary>
public sealed class OpenIdConnectProviderSet
{
    /// <summary>No provider.</summary>
    public static OpenIdConnectProviderSet Empty { get; } = new(Array.Empty<OpenIdConnectProvider>());

    /// <summary>Creates the set.</summary>
    public OpenIdConnectProviderSet(IReadOnlyList<OpenIdConnectProvider> providers)
        => All = providers ?? throw new ArgumentNullException(nameof(providers));

    /// <summary>Every configured provider, the legacy one first, then the named ones by id.</summary>
    public IReadOnlyList<OpenIdConnectProvider> All { get; }

    /// <summary>Whether any provider is configured.</summary>
    public bool Any => All.Count > 0;

    /// <summary>The provider with this id (case-insensitive), or null.</summary>
    public OpenIdConnectProvider? Find(string? id)
        => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
