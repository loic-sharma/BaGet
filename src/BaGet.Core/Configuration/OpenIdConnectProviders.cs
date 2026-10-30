using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BaGet.Core;

/// <summary>The rules for the OpenID Connect providers of <see cref="AccountsOptions"/>: resolving, ids and validation.</summary>
public static class OpenIdConnectProviders
{
    /// <summary>The id of the single <c>Accounts:OpenIdConnect</c> block.</summary>
    public const string LegacyId = "oidc";

    /// <summary>The configuration key whose children are the named providers.</summary>
    public const string SectionKey = "Accounts:OpenIdConnectProviders";

    /// <summary>The default claim holding the user name.</summary>
    public const string DefaultUserNameClaim = "preferred_username";

    /// <summary>The default claim holding the email address.</summary>
    public const string DefaultEmailClaim = "email";

    /// <summary>The default claim saying the address is verified.</summary>
    public const string DefaultEmailVerifiedClaim = "email_verified";

    private static readonly Regex IdPattern = new("^[a-z0-9-]{1,32}$", RegexOptions.CultureInvariant);

    /// <summary>Why <paramref name="id"/> cannot be a provider id, or null: lower-case letters, digits and <c>-</c>, 1 to 32 characters.</summary>
    public static string? ValidateId(string? id)
        => id != null && IdPattern.IsMatch(id) ? null : "A provider id has 1 to 32 characters: lower-case letters, digits and '-'.";

    /// <summary>
    /// The configured providers: the legacy block (when it has an authority and a client id) as <see cref="LegacyId"/>, then the named ones (ids are
    /// read case-insensitively and kept lower-case; one that lacks an authority or a client id is skipped).
    /// </summary>
    /// <exception cref="InvalidOperationException">An invalid or repeated id, <c>oidc</c> beside the legacy block, or inconsistent mappings.</exception>
    public static OpenIdConnectProviderSet Resolve(AccountsOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        var providers = new List<OpenIdConnectProvider>();
        var legacy = options.OpenIdConnect;
        if (legacy.IsConfigured)
        {
            providers.Add(new OpenIdConnectProvider(
                LegacyId,
                legacy.DisplayName,
                new OpenIdConnectProviderOptions
                {
                    Authority = legacy.Authority,
                    ClientId = legacy.ClientId,
                    ClientSecret = legacy.ClientSecret,
                    DisplayName = legacy.DisplayName,
                },
                IsLegacy: true));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in (options.OpenIdConnectProviders ?? new()).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var id = pair.Key.ToLowerInvariant();
            var problem = ValidateId(id);
            if (problem != null) throw new InvalidOperationException($"'{SectionKey}:{pair.Key}': {problem}");
            if (!seen.Add(id)) throw new InvalidOperationException($"'{SectionKey}:{pair.Key}': the provider id '{id}' is used twice (ids ignore case).");
            if (id == LegacyId && legacy.IsConfigured)
            {
                throw new InvalidOperationException(
                    $"'{SectionKey}:{pair.Key}': the id '{LegacyId}' belongs to the single 'Accounts:OpenIdConnect' block, which is configured too. Rename the provider or remove the block.");
            }

            var value = pair.Value ?? new OpenIdConnectProviderOptions();
            if (!value.IsConfigured) continue;

            var errors = Validate(value);
            if (errors.Count > 0) throw new InvalidOperationException($"'{SectionKey}:{pair.Key}': {string.Join(" ", errors)}");

            providers.Add(new OpenIdConnectProvider(id, string.IsNullOrWhiteSpace(value.DisplayName) ? id : value.DisplayName.Trim(), value, IsLegacy: false));
        }

        return new OpenIdConnectProviderSet(providers);
    }

    /// <summary>What is wrong with a provider's settings (an empty list when nothing is).</summary>
    public static IReadOnlyList<string> Validate(OpenIdConnectProviderOptions options)
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority) || (authority.Scheme != Uri.UriSchemeHttps && authority.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add("The authority must be an http(s) address.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId)) errors.Add("The client id is required.");

        var hasAdminClaim = !string.IsNullOrWhiteSpace(options.AdministratorClaim);
        if (hasAdminClaim != !string.IsNullOrWhiteSpace(options.AdministratorValue))
        {
            errors.Add("The administrator claim and the administrator value go together.");
        }

        var mappings = options.GroupMappings ?? new();
        if (mappings.Count > 0 && string.IsNullOrWhiteSpace(options.GroupClaim)) errors.Add("Group mappings need a group claim.");
        foreach (var pair in mappings)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Contains(':')) errors.Add($"A group mapping value ('{pair.Key}') is empty or holds ':'.");
            if (string.IsNullOrWhiteSpace(pair.Value)) errors.Add($"The group mapping for '{pair.Key}' names no group.");
        }

        foreach (var name in (options.Parameters ?? new()).Keys)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains(':')) errors.Add($"A request parameter name ('{name}') is empty or holds ':'.");
        }

        return errors;
    }

    /// <summary>The extra scopes of a provider (split on spaces and semicolons, without duplicates).</summary>
    public static IReadOnlyList<string> ExtraScopes(OpenIdConnectProviderOptions options)
        => (options.Scopes ?? string.Empty).Split(new[] { ' ', ';', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The claim holding the user name, or the default.</summary>
    public static string UserNameClaim(OpenIdConnectProviderOptions options) => OrDefault(options.UserNameClaim, DefaultUserNameClaim);

    /// <summary>The claim holding the email address, or the default.</summary>
    public static string EmailClaim(OpenIdConnectProviderOptions options) => OrDefault(options.EmailClaim, DefaultEmailClaim);

    /// <summary>The claim saying the address is verified, or the default.</summary>
    public static string EmailVerifiedClaim(OpenIdConnectProviderOptions options) => OrDefault(options.EmailVerifiedClaim, DefaultEmailVerifiedClaim);

    private static string OrDefault(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
