using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace BaGet.Core;

/// <summary>
/// A setting as the Settings page shows it.
/// </summary>
/// <param name="Descriptor">The setting.</param>
/// <param name="EffectiveValue">
/// The value in effect, as text (a list one entry per line). Always null for a sensitive
/// setting: only <paramref name="HasValue"/> says whether one is set.
/// </param>
/// <param name="HasValue">The setting has a non-empty value.</param>
/// <param name="IsOverridden">The setting is saved in the overrides file.</param>
/// <param name="ShadowedBy">
/// What overrides the saved value ("environment variable", "command line", ...), or null.
/// While set, a saved value has no effect.
/// </param>
/// <param name="PendingRestart">A restart-only setting whose value changed since startup.</param>
public sealed record SettingState(
    SettingDescriptor Descriptor,
    string? EffectiveValue,
    bool HasValue,
    bool IsOverridden,
    string? ShadowedBy,
    bool PendingRestart);

/// <summary>
/// The outcome of <see cref="SettingsService.SaveAsync"/>.
/// </summary>
/// <param name="Succeeded">The settings were saved.</param>
/// <param name="Errors">Why they were not.</param>
public sealed record SettingsSaveResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    /// <summary>A successful result.</summary>
    public static SettingsSaveResult Success { get; } = new(true, Array.Empty<string>());

    /// <summary>A failed result.</summary>
    public static SettingsSaveResult Failed(IEnumerable<string> errors) => new(false, errors.ToList());
}

/// <summary>
/// Reads the settings' effective state and saves changes to the overrides file. Register it
/// as a singleton and resolve it at startup, so that the startup snapshot, against which
/// "pending restart" is judged, is taken at boot.
/// </summary>
public class SettingsService
{
    private readonly IConfiguration _configuration;
    private readonly SettingsCatalog _catalog;
    private readonly IReadOnlyList<SettingsSchemaRegistration> _registrations;
    private readonly IServiceProvider _services;
    private readonly SettingsOverridesStore? _store;
    private readonly ILogger<SettingsService> _logger;
    private readonly Dictionary<string, string> _startup = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    /// <summary>
    /// Creates the service and takes the startup snapshot of the restart-only settings.
    /// </summary>
    /// <param name="configuration">The application's configuration, which is an <see cref="IConfigurationRoot"/>.</param>
    /// <param name="catalog">The settings.</param>
    /// <param name="registrations">The options types, bound again to validate a candidate.</param>
    /// <param name="services">Resolves the registered <see cref="IValidateOptions{TOptions}"/>.</param>
    /// <param name="logger">Receives the audit entry of each save.</param>
    /// <param name="store">The overrides file, or null when it is not available.</param>
    public SettingsService(
        IConfiguration configuration,
        SettingsCatalog catalog,
        IEnumerable<SettingsSchemaRegistration> registrations,
        IServiceProvider services,
        ILogger<SettingsService> logger,
        SettingsOverridesStore? store = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _registrations = (registrations ?? throw new ArgumentNullException(nameof(registrations))).ToList();
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _store = store;

        foreach (var descriptor in _catalog.Descriptors.Where(d => d.RequiresRestart))
        {
            #pragma warning disable CS8601
            _startup[descriptor.Key] = ReadValue(descriptor);
            #pragma warning restore CS8601
        }
    }

    /// <summary>
    /// Whether settings can be saved: the overrides store is registered.
    /// </summary>
    public bool IsAvailable => _store != null;

    /// <summary>
    /// The overrides file's path, or null when <see cref="IsAvailable"/> is false.
    /// </summary>
    public string? OverridesPath => _store?.FilePath;

    /// <summary>
    /// The catalog this service reports on.
    /// </summary>
    public SettingsCatalog Catalog => _catalog;

    /// <summary>
    /// Why the overrides file could not be read by the last <see cref="GetState"/> (it is
    /// then treated as empty), or null.
    /// </summary>
    public string? OverridesError { get; private set; }

    /// <summary>
    /// The state of every setting.
    /// </summary>
    public IReadOnlyList<SettingState> GetState()
    {
        IReadOnlyDictionary<string, string> overrides = new Dictionary<string, string>();
        OverridesError = null;
        try
        {
            overrides = _store?.Read() ?? overrides;
        }
        catch (Exception e) when (IsStoreFailure(e))
        {
            OverridesError = e.Message;
        }

        var root = _configuration as IConfigurationRoot;
        var overridesIndex = root == null ? -1 : FindOverridesProvider(root.Providers.ToList());

        return _catalog.Descriptors.Select(d => StateOf(d, overrides, root, overridesIndex)).ToList();
    }

    /// <summary>
    /// Validates and saves changes. Only failures the changes introduce are reported, so an
    /// unused provider's incomplete options cannot block an unrelated save.
    /// </summary>
    /// <remarks>
    /// The token is observed only before the overrides file is replaced (the save lock, the write of the temporary file);
    /// the replacement is one synchronous rename. An <see cref="OperationCanceledException"/> therefore means the file
    /// was not changed.
    /// </remarks>
    /// <param name="changes">The new value of each changed setting; null removes the override.</param>
    /// <param name="actor">Who is saving, for the audit log.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public async Task<SettingsSaveResult> SaveAsync(
        IReadOnlyDictionary<SettingDescriptor, object> changes,
        string? actor,
        CancellationToken cancellationToken)
    {
        if (changes == null) throw new ArgumentNullException(nameof(changes));
        if (_store == null) return SettingsSaveResult.Failed(new[] { "Saving settings is not available." });
        if (changes.Count == 0) return SettingsSaveResult.Success;

        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_configuration is not IConfigurationRoot root)
            {
                return SettingsSaveResult.Failed(new[] { "The configuration cannot be reloaded." });
            }

            var providers = root.Providers.ToList();
            IReadOnlyDictionary<string, string> current;
            try
            {
                current = _store.Read();
            }
            catch (Exception e) when (IsStoreFailure(e))
            {
                return SettingsSaveResult.Failed(new[] { e.Message });
            }

            var candidate = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes)
            {
                foreach (var key in candidate.Keys.Where(k => IsKeyOrChild(k, change.Key.Key)).ToList())
                {
                    candidate.Remove(key);
                }

                foreach (var pair in SettingsOverridesStore.Flatten(change.Key.Key, change.Value))
                {
                    candidate[pair.Key] = pair.Value;
                }
            }

            var before = Validate(BuildRoot(providers, current));
            var introduced = Validate(BuildRoot(providers, candidate)).Where(f => !before.Contains(f)).ToList();
            if (introduced.Count > 0) return SettingsSaveResult.Failed(introduced);

            try
            {
                await _store.WriteAsync(changes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (IsStoreFailure(e))
            {
                _logger.LogError(e, "Saving settings to {Path} failed", _store.FilePath);
                return SettingsSaveResult.Failed(new[]
                {
                    $"The settings could not be saved to \"{_store.FilePath}\": {e.Message} "
                    + "Make that location writable, or set SettingsOverridesPath to a writable file.",
                });
            }

            root.Reload();

            // Keys only: a value may be a credential.
            _logger.LogWarning(
                "{Actor} changed settings {Keys}",
                actor,
                string.Join(", ", changes.Keys.Select(k => k.Key)));

            return SettingsSaveResult.Success;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// The names of the children of <paramref name="sectionKey"/> (such as <see cref="OpenIdConnectProviders.SectionKey"/>) that the overrides file holds,
    /// so a page can tell what it may edit or remove from what a configuration file or the environment sets.
    /// </summary>
    public IReadOnlyCollection<string> GetSavedChildNames(string sectionKey)
    {
        try
        {
            var prefix = sectionKey + ":";

            return (_store?.Read() ?? new Dictionary<string, string>())
                .Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(k => k.Substring(prefix.Length).Split(':')[0])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (IsStoreFailure(e))
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// The per-source log levels now in effect (<see cref="LogLevelOverrides.SectionKey"/>), by source name, and whether the
    /// overrides file holds each. A level set by a configuration file or the environment is listed too.
    /// </summary>
    public IReadOnlyList<LogLevelOverride> GetLogLevelOverrides()
    {
        HashSet<string> saved;
        try
        {
            var prefix = LogLevelOverrides.SectionKey + ":";
            saved = (_store?.Read() ?? new Dictionary<string, string>())
                .Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(k => k.Substring(prefix.Length))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (IsStoreFailure(e))
        {
            saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return _configuration.GetSection(LogLevelOverrides.SectionKey).GetChildren()
            .Where(c => !string.IsNullOrEmpty(c.Value))
            .Select(c => new LogLevelOverride(c.Key, c.Value!, saved.Contains(c.Key)))
            .OrderBy(o => o.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Sets the lowest level logged for <paramref name="source"/> (applies live) or, with a null <paramref name="level"/>,
    /// removes the override this service saved. An override a configuration file or the environment sets cannot be removed
    /// from here, only given another level (the overrides file ranks above the configuration files).
    /// </summary>
    /// <param name="source">A namespace or type name prefix, validated by <see cref="LogLevelOverrides.ValidateSource"/>.</param>
    /// <param name="level">A Serilog level name (any case), or null to remove.</param>
    /// <param name="actor">Who is saving, for the audit log.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public Task<SettingsSaveResult> SetLogLevelOverrideAsync(string? source, string? level, string? actor, CancellationToken cancellationToken)
    {
        var problem = LogLevelOverrides.ValidateSource(source);
        if (problem != null) return Task.FromResult(SettingsSaveResult.Failed(new[] { problem }));

        var key = LogLevelOverrides.KeyOf(source!);
        object value;
        if (level == null)
        {
            var existing = GetLogLevelOverrides().FirstOrDefault(o => string.Equals(o.Source, source, StringComparison.OrdinalIgnoreCase));
            if (existing is { InOverridesFile: false })
            {
                return Task.FromResult(SettingsSaveResult.Failed(new[]
                {
                    $"The level of {existing.Source} is set outside the overrides file (a configuration file or the environment), so it cannot be removed here; change its level instead.",
                }));
            }

            value = null!;
        }
        else
        {
            var normalized = LogLevelOverrides.NormalizeLevel(level);
            if (normalized == null)
            {
                return Task.FromResult(SettingsSaveResult.Failed(new[] { $"\"{level}\" is not a level; use one of {string.Join(", ", LogLevelOverrides.Levels)}." }));
            }

            value = normalized;
        }

        var descriptor = new SettingDescriptor(key, "Serilog", "Serilog", SettingKind.Enum, typeof(string), LogLevelOverrides.Levels, false, false, "The lowest level logged for one source.");
        return SaveAsync(new Dictionary<SettingDescriptor, object> { [descriptor] = value }, actor, cancellationToken);
    }

    private SettingState StateOf(
        SettingDescriptor descriptor,
        IReadOnlyDictionary<string, string> overrides,
        IConfigurationRoot? root,
        int overridesIndex)
    {
        var configured = ReadConfigured(_configuration, descriptor);
        var value = configured ?? DefaultValue(descriptor);
        var isOverridden = overrides.Keys.Any(k => IsKeyOrChild(k, descriptor.Key));

        string? shadowedBy = null;
        if (root != null && overridesIndex >= 0)
        {
            var providers = root.Providers.ToList();
            for (var i = providers.Count - 1; i > overridesIndex; i--)
            {
                if (Has(providers[i], descriptor))
                {
                    shadowedBy = Describe(providers[i]);
                    break;
                }
            }
        }

        var pending = descriptor.RequiresRestart
                      && _startup.TryGetValue(descriptor.Key, out var started)
                      && !string.Equals(started ?? string.Empty, value ?? string.Empty, StringComparison.Ordinal);

        return new SettingState(
            descriptor,
            descriptor.IsSensitive ? null : value,
            !string.IsNullOrEmpty(configured),
            isOverridden,
            shadowedBy,
            pending);
    }

    private static bool Has(IConfigurationProvider provider, SettingDescriptor descriptor)
        => provider.TryGet(descriptor.Key, out _)
           || (descriptor.Kind == SettingKind.StringList && provider.TryGet(descriptor.Key + ":0", out _));

    private static string Describe(IConfigurationProvider provider)
    {
        var name = provider.GetType().Name;
        if (name.StartsWith("EnvironmentVariables", StringComparison.Ordinal)) return "environment variable";
        if (name.StartsWith("CommandLine", StringComparison.Ordinal)) return "command line";
        if (name.StartsWith("Chained", StringComparison.Ordinal)) return "host setting";
        if (name.StartsWith("Memory", StringComparison.Ordinal)) return "host setting";

        return name.EndsWith("ConfigurationProvider", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "ConfigurationProvider".Length)
            : name;
    }

    private int FindOverridesProvider(IReadOnlyList<IConfigurationProvider> providers)
    {
        if (_store == null) return -1;

        var full = System.IO.Path.GetFullPath(_store.FilePath);
        for (var i = 0; i < providers.Count; i++)
        {
            if (providers[i] is FileConfigurationProvider file
                && file.Source.FileProvider?.GetFileInfo(file.Source.Path ?? string.Empty) is { PhysicalPath: { } physical }
                && string.Equals(System.IO.Path.GetFullPath(physical), full, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsKeyOrChild(string candidate, string key)
        => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)
           || candidate.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase);

    private static bool IsStoreFailure(Exception e)
        => e is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException;

    /// <summary>
    /// The value in effect: the configured one, or when no provider sets the key, the options
    /// type's default (a default-true boolean is on although no file mentions it).
    /// </summary>
    private string? ReadValue(SettingDescriptor descriptor)
        => ReadConfigured(_configuration, descriptor) ?? DefaultValue(descriptor);

    private string? DefaultValue(SettingDescriptor descriptor)
    {
        foreach (var registration in _registrations)
        {
            var path = descriptor.Key.Split(':');
            var skip = 0;
            if (registration.Section != null)
            {
                var prefix = registration.Section.Split(':');
                if (path.Length <= prefix.Length
                    || !prefix.SequenceEqual(path.Take(prefix.Length), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                skip = prefix.Length;
            }

            object? instance;
            try
            {
                instance = Activator.CreateInstance(registration.OptionsType);
            }
            catch (Exception e) when (e is MissingMethodException or System.Reflection.TargetInvocationException)
            {
                continue;
            }

            foreach (var segment in path.Skip(skip))
            {
                if (instance == null) break;

                var property = instance.GetType().GetProperty(
                    segment,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (property == null || property.GetIndexParameters().Length > 0)
                {
                    instance = null;
                    break;
                }

                instance = property.GetValue(instance);
            }

            if (instance == null) continue;

            return FormatDefault(instance);
        }

        return null;
    }

    private static string? FormatDefault(object value)
        => value switch
        {
            bool flag => flag ? "true" : "false",
            string text => text,
            TimeSpan span => span.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            IDictionary => null,
            IEnumerable list => FormatList(list),
            _ => value.ToString(),
        };

    private static string? FormatList(IEnumerable list)
    {
        var entries = list.Cast<object>().Where(o => o != null).Select(o => o.ToString()).ToList();
        return entries.Count == 0 ? null : string.Join("\n", entries);
    }

    private static string? ReadConfigured(IConfiguration configuration, SettingDescriptor descriptor)
    {
        if (descriptor.Kind != SettingKind.StringList) return configuration[descriptor.Key];

        var entries = configuration.GetSection(descriptor.Key).GetChildren()
            .Where(c => c.Value != null)
            .OrderBy(c => int.TryParse(c.Key, out var n) ? n : int.MaxValue)
            .Select(c => c.Value)
            .ToList();

        return entries.Count == 0 ? null : string.Join("\n", entries);
    }

    /// <summary>
    /// A configuration with every provider as it is now, except the overrides file's, which is
    /// replaced by <paramref name="overrides"/>. The real providers are wrapped, so building and
    /// disposing it neither reloads nor disposes them.
    /// </summary>
    private IConfigurationRoot BuildRoot(IReadOnlyList<IConfigurationProvider> providers, IReadOnlyDictionary<string, string> overrides)
    {
        var overridesIndex = FindOverridesProvider(providers);
        var wrapped = new List<IConfigurationProvider>();
        for (var i = 0; i < providers.Count; i++)
        {
            wrapped.Add(i == overridesIndex
                ? new MemorySnapshotProvider(overrides)
                : new FrozenProvider(providers[i]));
        }

        // A host that did not insert the overrides source: the candidate is added last.
        if (overridesIndex < 0) wrapped.Add(new MemorySnapshotProvider(overrides));

        return new ConfigurationRoot(wrapped);
    }

    private HashSet<string> Validate(IConfigurationRoot configuration)
    {
        var failures = new HashSet<string>(StringComparer.Ordinal);

        foreach (var registration in _registrations)
        {
            object? instance;
            try
            {
                instance = Activator.CreateInstance(registration.OptionsType);
                var section = registration.Section == null ? (IConfiguration)configuration : configuration.GetSection(registration.Section);
                section.Bind(instance);
            }
            catch (Exception e) when (e is InvalidOperationException or MissingMethodException or ArgumentException)
            {
                failures.Add($"{registration.Section ?? "(root)"}: {e.Message}");
                continue;
            }

            var validatorType = typeof(IEnumerable<>).MakeGenericType(typeof(IValidateOptions<>).MakeGenericType(registration.OptionsType));
            var validators = (IEnumerable?)_services.GetService(validatorType);
            if (validators == null) continue;

            foreach (var validator in validators)
            {
                var method = validator.GetType().GetInterfaces()
                    .First(i => i.IsGenericType
                                && i.GetGenericTypeDefinition() == typeof(IValidateOptions<>)
                                && i.GetGenericArguments()[0] == registration.OptionsType)
                    .GetMethod(nameof(IValidateOptions<object>.Validate));

                var result = (ValidateOptionsResult?)method?.Invoke(validator, new[] { Options.DefaultName, instance });
                #pragma warning disable CS8602
                if (result.Failed)
                #pragma warning restore CS8602
                {
                    foreach (var failure in result.Failures) failures.Add(failure);
                }
            }
        }

        return failures;
    }

    private sealed class FrozenProvider : IConfigurationProvider
    {
        private readonly IConfigurationProvider _inner;

        public FrozenProvider(IConfigurationProvider inner) => _inner = inner;

        public bool TryGet(string key, out string? value) => _inner.TryGet(key, out value);

        public void Set(string key, string? value)
        {
        }

        public IChangeToken GetReloadToken() => new ConfigurationReloadToken();

        public void Load()
        {
        }

        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
            => _inner.GetChildKeys(earlierKeys, parentPath);
    }

    private sealed class MemorySnapshotProvider : ConfigurationProvider
    {
        public MemorySnapshotProvider(IReadOnlyDictionary<string, string> values)
        {
            foreach (var pair in values) Data[pair.Key] = pair.Value;
        }
    }
}
