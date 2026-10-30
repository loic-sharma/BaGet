using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using BaGet.Identity.Application.Models.Settings;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Identity.Application.UseCases.Admin;

/// <summary>
/// Edits every registered setting. Values are written to the overrides file; see <see cref="SettingsService"/>. One form per
/// section, so one section's errors never block another.
/// </summary>
public sealed class AdminSettingsUseCase : AdminUseCaseBase
{
    private static readonly ScreenRef Screen = ScreenCatalog.AdminSettings;

    // The section of the Settings screen that carries the "Sign-in providers" panel.
    private const string ProvidersSection = "Accounts";

    private readonly SettingsService _settings;
    private readonly DatabaseCopyService _copier;
    private readonly IConfiguration _configuration;
    private readonly ITelemetryDiagnostics? _telemetry;
    private readonly SignInProviderSettings _providers;

    /// <summary>Creates the use case.</summary>
    public AdminSettingsUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        SettingsService settings,
        DatabaseCopyService copier,
        IConfiguration configuration,
        MaintenanceGate? gate = null,
        ITelemetryDiagnostics? telemetry = null)
        : this(users, confirmation, settings, copier, configuration, gate, telemetry, new SignInProviderSettings(settings, configuration, OpenIdConnectProviderSet.Empty))
    {
    }

    /// <summary>Creates the use case.</summary>
    public AdminSettingsUseCase(
        UserManager<BaGetUser> users,
        PasswordConfirmation confirmation,
        SettingsService settings,
        DatabaseCopyService copier,
        IConfiguration configuration,
        MaintenanceGate? gate,
        ITelemetryDiagnostics? telemetry,
        SignInProviderSettings providers)
        : base(users, confirmation, gate)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _copier = copier ?? throw new ArgumentNullException(nameof(copier));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _telemetry = telemetry;
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    /// <summary>Shows the settings. <paramref name="section"/> is the id of the section to show (no JavaScript needed).</summary>
    public async Task<ScreenResult<AdminSettingsViewModel>> GetAsync(ClaimsPrincipal caller, string? section)
    {
        if (!_settings.IsAvailable) return ScreenResult<AdminSettingsViewModel>.NotFound();

        var model = new AdminSettingsViewModel();
        await LoadAsync(model, caller);
        Select(model, section);

        return ScreenResult<AdminSettingsViewModel>.Ok(model);
    }

    /// <summary>The progress of the database copy, polled by the page. Administrators only (the caller's gate); holds no connection string.</summary>
    public ScreenResult<DatabaseCopyStatusView> GetCopyStatus()
        => _settings.IsAvailable
            ? ScreenResult<DatabaseCopyStatusView>.Ok(DatabaseCopyStatusView.Of(_copier.Status))
            : ScreenResult<DatabaseCopyStatusView>.NotFound();

    /// <summary>
    /// Saves the settings of one section after the administrator confirms. Refused with HTTP 503 while the
    /// <see cref="MaintenanceGate"/> is closed. A Database section that changes to another relational database with "Copy all
    /// data" ticked is queued as a copy (<see cref="DatabaseCopyService"/>) instead of saved: the settings are saved by the copy
    /// once it is verified. A refusal shows the screen again (<see cref="ScreenStatus.Invalid"/>).
    /// </summary>
    public async Task<ScreenResult<AdminSettingsViewModel>> SaveAsync(ClaimsPrincipal caller, SettingsSubmission submission, CancellationToken cancellationToken)
    {
        if (!_settings.IsAvailable) return ScreenResult<AdminSettingsViewModel>.NotFound();

        // Saving writes a file, not the database, so the SaveChanges gate would not stop it; but a save
        // while the database is read-only for maintenance (a copy, or the wait for a restart) could
        // change the very settings the job depends on. The job itself saves through SettingsService.
        ThrowIfClosed();

        var section = submission.Section ?? string.Empty;
        var user = await CurrentAsync(caller);
        if (user == null) return ScreenResult<AdminSettingsViewModel>.SignIn();

        if (!await Confirmation.IsConfirmedAsync(user, submission.Password))
        {
            return await FailAsync(caller, section, new[] { "The confirmation is incorrect, so nothing was changed." });
        }

        var states = _settings.GetState()
            .Where(s => string.Equals(s.Descriptor.Section, section, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var (changes, parseErrors) = SettingsForm.Parse(
            submission.Fields.Select(pair => new KeyValuePair<string, StringValues>(pair.Key, new StringValues(pair.Value))),
            states);

        if (parseErrors.Count == 0
            && string.Equals(section, "Database", StringComparison.OrdinalIgnoreCase)
            && submission.Field("copyData").Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase))
            && PlanCopy(changes, user.UserName) is { } copy)
        {
            if (!_copier.TryQueue(copy, out var refusal)) return await FailAsync(caller, section, new[] { refusal! });

            return ToSection(section, "Copying the data to the new database. The settings are saved when the copy is verified.");
        }

        var result = parseErrors.Count == 0
            ? await _settings.SaveAsync(changes, user.UserName, cancellationToken)
            : SettingsSaveResult.Failed(parseErrors);
        if (!result.Succeeded) return await FailAsync(caller, section, result.Errors);

        return ToSection(section, changes.Count == 0 ? "Nothing changed." : $"Saved {changes.Count} setting(s).");
    }

    /// <summary>
    /// Adds or changes a named OpenID Connect provider (the "Sign-in providers" panel of the Accounts section). Confirmed with the password, refused with
    /// HTTP 503 while the <see cref="MaintenanceGate"/> is closed. A blank secret keeps the saved one; the provider is registered after a restart.
    /// </summary>
    public async Task<ScreenResult<AdminSettingsViewModel>> SaveSignInProviderAsync(ClaimsPrincipal caller, SignInProviderForm? form, CancellationToken cancellationToken)
    {
        if (!_settings.IsAvailable) return ScreenResult<AdminSettingsViewModel>.NotFound();

        ThrowIfClosed();

        var user = await CurrentAsync(caller);
        if (user == null) return ScreenResult<AdminSettingsViewModel>.SignIn();

        if (form == null || !await Confirmation.IsConfirmedAsync(user, form.Password))
        {
            return await FailAsync(caller, ProvidersSection, new[] { NotConfirmed });
        }

        var result = await _providers.SaveAsync(form, user.UserName, cancellationToken);
        if (!result.Succeeded) return await FailAsync(caller, ProvidersSection, result.Errors);

        return ToSection(ProvidersSection, $"Saved the provider {form.Id?.Trim()}. Restart BaGet to apply it.");
    }

    /// <summary>Removes a named provider saved on this screen (the same confirmation, gate and restart as <see cref="SaveSignInProviderAsync"/>).</summary>
    public async Task<ScreenResult<AdminSettingsViewModel>> RemoveSignInProviderAsync(ClaimsPrincipal caller, RemoveSignInProviderForm? form, CancellationToken cancellationToken)
    {
        if (!_settings.IsAvailable) return ScreenResult<AdminSettingsViewModel>.NotFound();

        ThrowIfClosed();

        var user = await CurrentAsync(caller);
        if (user == null) return ScreenResult<AdminSettingsViewModel>.SignIn();

        if (form == null || !await Confirmation.IsConfirmedAsync(user, form.Password))
        {
            return await FailAsync(caller, ProvidersSection, new[] { NotConfirmed });
        }

        var result = await _providers.RemoveAsync(form.Id, user.UserName, cancellationToken);
        if (!result.Succeeded) return await FailAsync(caller, ProvidersSection, result.Errors);

        return ToSection(ProvidersSection, $"Removed the provider {form.Id?.Trim()}. Restart BaGet to apply it.");
    }

    /// <summary>
    /// Sets the lowest level logged for one source (a namespace prefix); applies live. Confirmed with the password like a save,
    /// refused with HTTP 503 while the <see cref="MaintenanceGate"/> is closed.
    /// </summary>
    public Task<ScreenResult<AdminSettingsViewModel>> SetLogLevelAsync(ClaimsPrincipal caller, string? source, string? level, string? password, CancellationToken cancellationToken)
        => ChangeLogLevelAsync(caller, source?.Trim(), level ?? string.Empty, password, "set", cancellationToken);

    /// <summary>Removes a per-source level saved on this page (one a configuration file sets is only given another level).</summary>
    public Task<ScreenResult<AdminSettingsViewModel>> RemoveLogLevelAsync(ClaimsPrincipal caller, string? source, string? password, CancellationToken cancellationToken)
        => ChangeLogLevelAsync(caller, source?.Trim(), null, password, "removed", cancellationToken);

    private async Task<ScreenResult<AdminSettingsViewModel>> ChangeLogLevelAsync(
        ClaimsPrincipal caller,
        string? source,
        string? level,
        string? password,
        string verb,
        CancellationToken cancellationToken)
    {
        if (!_settings.IsAvailable) return ScreenResult<AdminSettingsViewModel>.NotFound();

        ThrowIfClosed();

        var user = await CurrentAsync(caller);
        if (user == null) return ScreenResult<AdminSettingsViewModel>.SignIn();

        if (!await Confirmation.IsConfirmedAsync(user, password))
        {
            return await FailAsync(caller, "Serilog", new[] { "The confirmation is incorrect, so nothing was changed." });
        }

        var result = await _settings.SetLogLevelOverrideAsync(source, level, user.UserName, cancellationToken);
        if (!result.Succeeded) return await FailAsync(caller, "Serilog", result.Errors);

        return ToSection(
            "Serilog",
            level == null
                ? $"The level of {source} was {verb}."
                : $"The level of {source} was {verb} to {LogLevelOverrides.NormalizeLevel(level)}.");
    }

    /// <summary>
    /// Sends one test span to the Telemetry section's <i>posted, unsaved</i> endpoint, protocol and headers and shows the
    /// outcome; nothing is saved. Confirmed like a save (a test sends the headers, which may hold a credential, to an address the
    /// administrator chose). A blank headers field keeps the saved headers unless "Clear" is ticked. Not refused while the
    /// <see cref="MaintenanceGate"/> is closed: it writes nothing.
    /// </summary>
    public async Task<ScreenResult<AdminSettingsViewModel>> TestTelemetryAsync(ClaimsPrincipal caller, SettingsSubmission submission, CancellationToken cancellationToken)
    {
        if (!_settings.IsAvailable || _telemetry == null) return ScreenResult<AdminSettingsViewModel>.NotFound();

        var user = await CurrentAsync(caller);
        if (user == null) return ScreenResult<AdminSettingsViewModel>.SignIn();

        if (!await Confirmation.IsConfirmedAsync(user, submission.Password))
        {
            return await FailAsync(caller, "Telemetry", new[] { "The confirmation is incorrect, so nothing was tested." });
        }

        string? Posted(string key) => submission.Has(SettingsForm.ValuePrefix + key) ? submission.Field(SettingsForm.ValuePrefix + key).ToString().Trim() : null;

        var headers = submission.Has(SettingsForm.ClearPrefix + "Telemetry:Otlp:Headers")
            ? string.Empty
            : Posted("Telemetry:Otlp:Headers") is { Length: > 0 } typed ? typed : null;

        var model = new AdminSettingsViewModel
        {
            TelemetryTest = await _telemetry.TestAsync(
                Posted("Telemetry:Otlp:Endpoint"),
                Posted("Telemetry:Otlp:Protocol"),
                headers,
                cancellationToken),
        };
        await LoadAsync(model, caller);
        Select(model, null, "Telemetry", locked: true);

        return ScreenResult<AdminSettingsViewModel>.Ok(model);
    }

    private static void Select(AdminSettingsViewModel model, string? requested, string? failedSection = null, bool locked = false)
    {
        string? Find(string? value) => value == null ? null : model.Ids.FirstOrDefault(i => string.Equals(i, value, StringComparison.OrdinalIgnoreCase));

        var fromKey = failedSection != null && model.TryGetId(failedSection, out var failed)
            ? failed
            : failedSection != null ? Find(failedSection) : null;

        model.SelectionLocked = locked && fromKey != null;
        model.SelectedId = fromKey ?? Find(requested) ?? (model.Sections.Count > 0 ? model.SectionId(model.Sections[0].Key) : string.Empty);
    }

    /// <summary>Back to the screen showing <paramref name="section"/>: <c>?section=id</c> (the server renders it) and <c>#id</c> (the browser keeps it).</summary>
    private static ScreenResult<AdminSettingsViewModel> ToSection(string section, string message)
    {
        var id = AdminSettingsViewModel.BuildId(section);

        return ScreenResult<AdminSettingsViewModel>.GoTo(new ScreenNext(Screen, Route(("section", id)), message, Fragment: id));
    }

    /// <summary>
    /// The copy a Database section asks for, or null when it is an ordinary save: neither the type nor the
    /// connection string is set to a new value, either database is not relational (AzureTable), or it is the
    /// database already running.
    /// </summary>
    private DatabaseCopyRequest? PlanCopy(IReadOnlyDictionary<SettingDescriptor, object> changes, string? actor)
    {
        string? Changed(string key, out bool reset)
        {
            reset = false;
            foreach (var change in changes)
            {
                if (!string.Equals(change.Key.Key, key, StringComparison.OrdinalIgnoreCase)) continue;

                reset = change.Value == null;
                return change.Value as string;
            }

            return null;
        }

        var type = Changed("Database:Type", out var typeReset);
        var connection = Changed("Database:ConnectionString", out var connectionReset);
        if (typeReset || connectionReset) return null;
        if (type == null && connection == null) return null;

        type ??= _configuration["Database:Type"];
        connection ??= _configuration["Database:ConnectionString"];
        var mySqlVersion = Changed("Database:MySqlServerVersion", out _) ?? _configuration["Database:MySqlServerVersion"];

        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(connection)) return null;
        if (!_copier.IsRelational(_copier.CurrentRelationalType) || !_copier.IsRelational(type)) return null;
        if (_copier.IsCurrentDatabase(type, connection)) return null;

        return new DatabaseCopyRequest(type, connection, mySqlVersion, changes, actor);
    }

    private async Task<ScreenResult<AdminSettingsViewModel>> FailAsync(ClaimsPrincipal caller, string section, IReadOnlyList<string> errors)
    {
        var model = new AdminSettingsViewModel { Errors = errors, ErrorSection = section };
        await LoadAsync(model, caller);
        Select(model, null, section, locked: true);

        return ScreenResult<AdminSettingsViewModel>.Invalid(
            model,
            new Dictionary<string, string[]> { [string.Empty] = errors.ToArray() });
    }

    private async Task LoadAsync(AdminSettingsViewModel model, ClaimsPrincipal caller)
    {
        var states = _settings.GetState();
        model.LoadError = _settings.OverridesError;
        model.OverridesPath = _settings.OverridesPath;

        model.Sections = states
            .GroupBy(s => s.Descriptor.Section ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key.Length == 0 ? 0 : 1)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in model.Sections)
        {
            var id = AdminSettingsViewModel.BuildId(group.Key);
            var candidate = id;
            for (var n = 2; !used.Add(candidate); n++) candidate = id + "-" + n;
            ids[group.Key] = candidate;
        }
        model.SetIds(ids);
        model.LogLevels = _settings.GetLogLevelOverrides();
        model.TelemetrySignals = _telemetry?.GetStatus();
        model.SignInProviders = _providers.List();
        model.PendingRestart = states.Where(s => s.PendingRestart).Select(s => s.Descriptor.Key).Concat(_providers.PendingRestartKeys()).ToList();
        model.CanCopyDatabase = _copier.CurrentRelationalType != null;
        model.CopyTargetTypes = _copier.RelationalTypes;
        model.CopyStatus = _copier.Status;

        await Confirmation.LoadAsync(model, caller);
    }
}
