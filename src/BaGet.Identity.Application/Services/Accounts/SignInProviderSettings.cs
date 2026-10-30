using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Models.Settings;
using Microsoft.Extensions.Configuration;

namespace BaGet.Identity.Application.Services.Accounts;

/// <summary>
/// The named OpenID Connect providers the Settings screen edits: lists them (what is saved, against what the running server registered), and adds,
/// changes or removes one by writing <c>Accounts:OpenIdConnectProviders:&lt;id&gt;:*</c> to the overrides file through <see cref="SettingsService"/>.
/// Handlers are registered at startup, so every change needs a restart. Only providers the overrides file holds can be changed here.
/// </summary>
public sealed class SignInProviderSettings
{
    private static readonly JsonSerializerOptions Fingerprint = new() { WriteIndented = false };

    private readonly SettingsService _settings;
    private readonly IConfiguration _configuration;
    private readonly OpenIdConnectProviderSet _active;

    /// <summary>Creates the service.</summary>
    public SignInProviderSettings(SettingsService settings, IConfiguration configuration, OpenIdConnectProviderSet active)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _active = active ?? throw new ArgumentNullException(nameof(active));
    }

    /// <summary>The named providers: those configured now (saved or from the configuration) and those the running server still has.</summary>
    public IReadOnlyList<SignInProviderView> List()
    {
        var configured = ReadConfigured();
        var saved = _settings.GetSavedChildNames(OpenIdConnectProviders.SectionKey);
        var ids = configured.Keys.Concat(_active.All.Where(p => !p.IsLegacy).Select(p => p.Id)).Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal);

        var result = new List<SignInProviderView>();
        foreach (var id in ids)
        {
            var active = _active.Find(id) is { IsLegacy: false } found ? found : null;
            var options = configured.TryGetValue(id, out var current) ? current : active!.Options;
            var view = ViewOf(id, options);
            view.Editable = saved.Contains(id);
            view.Active = active != null;
            view.PendingRestart = Differs(configured.TryGetValue(id, out var now) ? now : null, active?.Options);
            result.Add(view);
        }

        return result;
    }

    /// <summary>The ids that differ from what the running server registered (added, changed or removed since startup).</summary>
    public IReadOnlyList<string> PendingRestartKeys() => List().Where(p => p.PendingRestart).Select(p => OpenIdConnectProviders.SectionKey + ":" + p.Id).ToList();

    /// <summary>Adds or changes the provider named by the form after validating it. The result says why nothing was saved.</summary>
    public async Task<SettingsSaveResult> SaveAsync(SignInProviderForm form, string? actor, CancellationToken cancellationToken)
    {
        var id = form.Id?.Trim() ?? string.Empty;
        var problem = OpenIdConnectProviders.ValidateId(id);
        if (problem != null) return SettingsSaveResult.Failed(new[] { problem });

        var configured = ReadConfigured();
        var saved = _settings.GetSavedChildNames(OpenIdConnectProviders.SectionKey);
        var exists = configured.ContainsKey(id);
        if (exists && !saved.Contains(id))
        {
            return SettingsSaveResult.Failed(new[] { $"The provider '{id}' is set by a configuration file or the environment, so it cannot be changed here." });
        }

        if (!exists && id == OpenIdConnectProviders.LegacyId && _active.All.Any(p => p.IsLegacy))
        {
            return SettingsSaveResult.Failed(new[] { $"The id '{id}' belongs to the single 'Accounts:OpenIdConnect' block, which is configured. Choose another id." });
        }

        var parameters = ParseLines(form.Parameters, "parameter", StringComparer.OrdinalIgnoreCase, out var parameterErrors);
        var mappings = ParseLines(form.GroupMappings, "group mapping", StringComparer.OrdinalIgnoreCase, out var mappingErrors);
        var errors = parameterErrors.Concat(mappingErrors).ToList();

        var candidate = new OpenIdConnectProviderOptions
        {
            Authority = Clean(form.Authority),
            ClientId = Clean(form.ClientId),
            AdministratorClaim = Clean(form.AdministratorClaim),
            AdministratorValue = Clean(form.AdministratorValue),
            GroupClaim = Clean(form.GroupClaim),
            Parameters = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase),
            GroupMappings = new Dictionary<string, string>(mappings, StringComparer.Ordinal),
        };
        errors.AddRange(OpenIdConnectProviders.Validate(candidate));
        if (errors.Count > 0) return SettingsSaveResult.Failed(errors.Distinct());

        var prefix = OpenIdConnectProviders.SectionKey + ":" + id;
        var changes = new Dictionary<SettingDescriptor, object>();
        void Set(string field, string? value, bool sensitive = false) => changes[Describe(prefix + ":" + field, sensitive)] = value!;

        Set("DisplayName", Clean(form.DisplayName));
        Set("Authority", candidate.Authority);
        Set("ClientId", candidate.ClientId);
        if (!string.IsNullOrEmpty(form.ClientSecret)) Set("ClientSecret", form.ClientSecret, sensitive: true);
        Set("Scopes", Clean(form.Scopes));
        Set("UserNameClaim", Clean(form.UserNameClaim));
        Set("EmailClaim", Clean(form.EmailClaim));
        Set("EmailVerifiedClaim", Clean(form.EmailVerifiedClaim));
        Set("AdministratorClaim", candidate.AdministratorClaim);
        Set("AdministratorValue", candidate.AdministratorValue);
        Set("GroupClaim", candidate.GroupClaim);
        changes[Describe(prefix + ":Parameters")] = null!;
        foreach (var pair in parameters) Set("Parameters:" + pair.Key, pair.Value);
        changes[Describe(prefix + ":GroupMappings")] = null!;
        foreach (var pair in mappings) Set("GroupMappings:" + pair.Key, pair.Value);

        return await _settings.SaveAsync(changes, actor, cancellationToken);
    }

    /// <summary>Removes a provider the overrides file holds.</summary>
    public async Task<SettingsSaveResult> RemoveAsync(string? id, string? actor, CancellationToken cancellationToken)
    {
        id = id?.Trim() ?? string.Empty;
        if (!_settings.GetSavedChildNames(OpenIdConnectProviders.SectionKey).Contains(id))
        {
            return SettingsSaveResult.Failed(new[]
            {
                ReadConfigured().ContainsKey(id)
                    ? $"The provider '{id}' is set by a configuration file or the environment, so it cannot be removed here."
                    : $"There is no saved provider '{id}'.",
            });
        }

        var changes = new Dictionary<SettingDescriptor, object> { [Describe(OpenIdConnectProviders.SectionKey + ":" + id)] = null! };

        return await _settings.SaveAsync(changes, actor, cancellationToken);
    }

    private Dictionary<string, OpenIdConnectProviderOptions> ReadConfigured()
    {
        var result = new Dictionary<string, OpenIdConnectProviderOptions>(StringComparer.Ordinal);
        foreach (var child in _configuration.GetSection(OpenIdConnectProviders.SectionKey).GetChildren())
        {
            var options = child.Get<OpenIdConnectProviderOptions>() ?? new OpenIdConnectProviderOptions();
            result[child.Key.ToLowerInvariant()] = options;
        }

        return result;
    }

    private static SettingDescriptor Describe(string key, bool sensitive = false)
        => new(key, "Accounts", "Accounts", SettingKind.String, typeof(string), Array.Empty<string>(), sensitive, true, "A sign-in provider.");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SignInProviderView ViewOf(string id, OpenIdConnectProviderOptions options) => new()
    {
        Id = id,
        DisplayName = options.DisplayName,
        Authority = options.Authority,
        ClientId = options.ClientId,
        HasSecret = !string.IsNullOrEmpty(options.ClientSecret),
        Scopes = options.Scopes,
        Parameters = string.Join("\n", options.Parameters.Select(p => p.Key + "=" + p.Value)),
        UserNameClaim = options.UserNameClaim,
        EmailClaim = options.EmailClaim,
        EmailVerifiedClaim = options.EmailVerifiedClaim,
        AdministratorClaim = options.AdministratorClaim,
        AdministratorValue = options.AdministratorValue,
        GroupClaim = options.GroupClaim,
        GroupMappings = string.Join("\n", options.GroupMappings.Select(p => p.Key + "=" + p.Value)),
    };

    // Whether what is configured now is not what the running server registered; the secret takes part (a changed secret needs a restart) without being shown.
    private static bool Differs(OpenIdConnectProviderOptions? now, OpenIdConnectProviderOptions? running)
    {
        if (now is { IsConfigured: false }) now = null;
        if (now == null && running == null) return false;
        if (now == null || running == null) return true;

        return JsonSerializer.Serialize(Normalize(now), Fingerprint) != JsonSerializer.Serialize(Normalize(running), Fingerprint);
    }

    private static object Normalize(OpenIdConnectProviderOptions o) => new
    {
        o.Authority, o.ClientId, o.ClientSecret, DisplayName = o.DisplayName ?? string.Empty, o.Scopes, o.UserNameClaim, o.EmailClaim, o.EmailVerifiedClaim,
        o.AdministratorClaim, o.AdministratorValue, o.GroupClaim,
        Parameters = o.Parameters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key.ToLowerInvariant() + "=" + p.Value).ToArray(),
        GroupMappings = o.GroupMappings.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value).ToArray(),
    };

    private static Dictionary<string, string> ParseLines(string? text, string what, StringComparer comparer, out List<string> errors)
    {
        errors = new List<string>();
        var result = new Dictionary<string, string>(comparer);
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var at = line.IndexOf('=');
            if (at <= 0 || at == line.Length - 1)
            {
                errors.Add($"A {what} line is name=value (got '{line}').");
                continue;
            }

            var name = line[..at].Trim();
            if (!result.TryAdd(name, line[(at + 1)..].Trim())) errors.Add($"The {what} '{name}' is given twice (names that differ only by case are the same).");
        }

        return result;
    }
}
