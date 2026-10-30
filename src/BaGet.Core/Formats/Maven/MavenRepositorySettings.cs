using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BaGet.Core;

/// <summary>Which versions a local Maven repository accepts.</summary>
public enum MavenPolicy
{
    /// <summary>Release versions only; a <c>-SNAPSHOT</c> deploy is refused (409).</summary>
    Releases = 0,

    /// <summary>Snapshot versions only; a release deploy is refused (409).</summary>
    Snapshots = 1,

    /// <summary>Both.</summary>
    Both = 2,
}

/// <summary>
/// The settings of a local Maven repository, kept as JSON in <see cref="Repository.Settings"/> (no schema of their own).
/// </summary>
public sealed class MavenRepositorySettings
{
    /// <summary>The largest <see cref="MaxSnapshotBuilds"/> the form accepts.</summary>
    public const int MaxSnapshotBuildsLimit = 1000;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Which versions are accepted. Default: releases only, as Maven Central's layout expects.</summary>
    public MavenPolicy Policy { get; set; } = MavenPolicy.Releases;

    /// <summary>
    /// Whether a release file that exists may be deployed again (replaced). The caller then also needs the Delete action, as
    /// Artifactory's Delete/Overwrite does. Default false: a release is immutable. Snapshots are never refused for this.
    /// </summary>
    public bool AllowRedeploy { get; set; }

    /// <summary>
    /// How many timestamped builds of a snapshot version are kept: when a deploy brings a newer build, older ones beyond this
    /// number are deleted. Zero keeps all (clean up on the artifact's page).
    /// </summary>
    public int MaxSnapshotBuilds { get; set; }

    /// <summary>Whether <paramref name="version"/> may be deployed under <see cref="Policy"/>.</summary>
    public bool Accepts(string version) => Policy switch
    {
        MavenPolicy.Releases => !MavenNames.IsSnapshot(version),
        MavenPolicy.Snapshots => MavenNames.IsSnapshot(version),
        _ => true,
    };

    /// <summary>The settings stored in <paramref name="json"/>; the defaults when it is null, empty or unreadable.</summary>
    public static MavenRepositorySettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new MavenRepositorySettings();

        try
        {
            var settings = JsonSerializer.Deserialize<MavenRepositorySettings>(json, Json) ?? new MavenRepositorySettings();
            settings.MaxSnapshotBuilds = Math.Clamp(settings.MaxSnapshotBuilds, 0, MaxSnapshotBuildsLimit);

            return settings;
        }
        catch (JsonException)
        {
            return new MavenRepositorySettings();
        }
    }

    /// <summary>The JSON to store.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// Checks the values (the form's keys: <c>policy</c>, <c>redeploy</c>, <c>snapshotBuilds</c>) and builds the settings.
    /// </summary>
    /// <exception cref="ArgumentException">A value is not valid; the message is shown to the administrator.</exception>
    public static MavenRepositorySettings FromFields(IReadOnlyDictionary<string, string?> fields)
    {
        if (fields == null) throw new ArgumentNullException(nameof(fields));

        var settings = new MavenRepositorySettings();
        var policy = Get(fields, "policy");
        if (!string.IsNullOrEmpty(policy))
        {
            if (!Enum.TryParse<MavenPolicy>(policy, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                throw new ArgumentException("The version policy must be Releases, Snapshots or Both.");
            }

            settings.Policy = parsed;
        }

        var redeploy = Get(fields, "redeploy");
        if (!string.IsNullOrEmpty(redeploy))
        {
            if (redeploy.Equals("true", StringComparison.OrdinalIgnoreCase) || redeploy.Equals("yes", StringComparison.OrdinalIgnoreCase)) settings.AllowRedeploy = true;
            else if (redeploy.Equals("false", StringComparison.OrdinalIgnoreCase) || redeploy.Equals("no", StringComparison.OrdinalIgnoreCase)) settings.AllowRedeploy = false;
            else throw new ArgumentException("Redeploying releases must be Yes or No.");
        }

        var builds = Get(fields, "snapshotBuilds");
        if (!string.IsNullOrEmpty(builds))
        {
            if (!int.TryParse(builds, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) || count > MaxSnapshotBuildsLimit)
            {
                throw new ArgumentException($"Snapshot builds kept must be a number from 0 (all) to {MaxSnapshotBuildsLimit}.");
            }

            settings.MaxSnapshotBuilds = count;
        }

        return settings;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> fields, string key)
        => fields.TryGetValue(key, out var value) ? value?.Trim() : null;
}
