using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.Logging;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// Applies a provider's role and group mapping when a user signs in through it. Only a provider with an administrator claim (and value) or a
/// group claim with mappings has an effect, and only on the users signing in through it. Everything goes through the existing services
/// (<see cref="AdministratorService"/>, <see cref="IAccessAdministration"/>) so the rules (never the last administrator) and the access-model cache
/// are the same as for an administrator doing it by hand, and every change is audited with the provider as actor.
/// </summary>
public sealed class ExternalProviderMapping
{
    private readonly AdministratorService _administrators;
    private readonly IAccessAdministration? _access;
    private readonly IAdminAudit _audit;
    private readonly MaintenanceGate? _gate;
    private readonly ILogger<ExternalProviderMapping> _logger;

    /// <summary>Creates the service.</summary>
    public ExternalProviderMapping(
        AdministratorService administrators,
        IAdminAudit audit,
        ILogger<ExternalProviderMapping> logger,
        IAccessAdministration? access = null,
        MaintenanceGate? gate = null)
    {
        _administrators = administrators ?? throw new ArgumentNullException(nameof(administrators));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _access = access;
        _gate = gate;
    }

    /// <summary>The name recorded as the actor of a change made by <paramref name="provider"/>.</summary>
    public static string ActorOf(OpenIdConnectProvider provider) => "provider:" + provider.Id;

    /// <summary>
    /// Brings <paramref name="user"/> in line with the claims the provider sent. Does nothing for a provider with no mapping, and while the
    /// read-only gate is closed (a sign-in must still work; the mapping applies at the next one).
    /// </summary>
    public async Task ApplyAsync(OpenIdConnectProvider provider, BaGetUser user, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var options = provider.Options;
        var hasAdministrator = !string.IsNullOrWhiteSpace(options.AdministratorClaim) && !string.IsNullOrWhiteSpace(options.AdministratorValue);
        var hasGroups = !string.IsNullOrWhiteSpace(options.GroupClaim) && options.GroupMappings is { Count: > 0 };
        if (!hasAdministrator && !hasGroups) return;

        if (_gate is { IsClosed: true })
        {
            _logger.LogWarning("The server is read-only: the mapping of provider {Provider} was not applied to {User}", provider.Id, user.UserName);

            return;
        }

        var actor = ActorOf(provider);
        if (hasAdministrator) await ApplyAdministratorAsync(provider, user, principal, actor, cancellationToken);
        if (hasGroups) await ApplyGroupsAsync(provider, user, principal, actor, cancellationToken);
    }

    private async Task ApplyAdministratorAsync(OpenIdConnectProvider provider, BaGetUser user, ClaimsPrincipal principal, string actor, CancellationToken cancellationToken)
    {
        var options = provider.Options;
        var present = ExternalClaims.Values(principal, options.AdministratorClaim).Contains(options.AdministratorValue!.Trim(), StringComparer.Ordinal);

        if (present == user.IsAdministrator) return;

        var change = present
            ? await _administrators.PromoteAsync(user.UserName, actor, cancellationToken)
            : await _administrators.DemoteAsync(user.UserName, actor, cancellationToken);

        switch (change)
        {
            case AdministratorChange.Changed:
                _audit.Record(actor, present ? "account.promote" : "account.demote", $"user {user.UserName}", $"administrator claim '{options.AdministratorClaim}' at provider {provider.Id}");
                break;
            case AdministratorChange.LastAdministrator:
                _logger.LogWarning("Provider {Provider} no longer vouches for administrator {User}, who is the last one: kept", provider.Id, user.UserName);
                break;
        }
    }

    private async Task ApplyGroupsAsync(OpenIdConnectProvider provider, BaGetUser user, ClaimsPrincipal principal, string actor, CancellationToken cancellationToken)
    {
        if (_access == null) return;

        var options = provider.Options;
        var values = ExternalClaims.Values(principal, options.GroupClaim).ToHashSet(StringComparer.Ordinal);

        // A group is wanted when any of the values mapped to it is present.
        var wanted = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in options.GroupMappings)
        {
            var group = mapping.Value.Trim();
            wanted[group] = wanted.TryGetValue(group, out var already) && already || values.Contains(mapping.Key);
        }

        IReadOnlyList<string> current;
        try
        {
            current = await _access.GroupsOfUserAsync(user.Id, cancellationToken);
        }
        catch (InvalidOperationException e)
        {
            _logger.LogWarning(e, "The groups of {User} could not be read: provider {Provider}'s group mapping was skipped", user.UserName, provider.Id);

            return;
        }

        var member = current.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (group, want) in wanted)
        {
            if (want == member.Contains(group)) continue;

            try
            {
                if (await _access.GetGroupAsync(group, cancellationToken) == null)
                {
                    _logger.LogWarning("Provider {Provider} maps to the group {Group}, which does not exist: skipped", provider.Id, group);
                    continue;
                }

                if (want) await _access.AddGroupMemberAsync(group, user.Id, actor, cancellationToken);
                else await _access.RemoveGroupMemberAsync(group, user.Id, actor, cancellationToken);
            }
            catch (AccessModelException e)
            {
                _logger.LogWarning(e, "Provider {Provider}'s mapping to the group {Group} could not be applied to {User}", provider.Id, group, user.UserName);
            }
        }
    }
}
