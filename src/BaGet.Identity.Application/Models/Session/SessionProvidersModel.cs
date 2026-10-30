using System.Collections.Generic;
using BaGet.Identity.Application.Models.Account;

namespace BaGet.Identity.Application.Models.Session;

/// <summary>The external sign-in providers a client can offer (<c>GET /api/ui/session/providers</c>): one button each, started at <c>/api/ui/session/external/{id}</c>.</summary>
public sealed class SessionProvidersModel
{
    /// <summary>The configured providers, empty when there is none.</summary>
    public IReadOnlyList<ExternalProviderInfo> Providers { get; set; } = new List<ExternalProviderInfo>();
}
