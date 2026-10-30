using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BaGet.Core;

/// <summary>
/// What a <see cref="RepositoryKind.Virtual"/> repository is made of: kept as JSON in <see cref="Repository.Settings"/>.
/// Members are named (names never change), so a member is found again whatever its key.
/// </summary>
public sealed class VirtualRepositorySettings
{
    /// <summary>Most members one virtual repository may list (each one costs a lookup per request).</summary>
    public const int MaxMembers = 50;

    /// <summary>
    /// The member repositories in resolution order: the first one that has a package version answers for it. A member
    /// may be a local, a remote or another virtual repository (flattened in place, cycles refused).
    /// </summary>
    [JsonPropertyName("members")]
    public List<string> Members { get; set; } = new();

    /// <summary>
    /// The member that receives what is pushed to the virtual repository (a local repository, or a virtual one that
    /// itself resolves to a local one). Null: the virtual repository takes no uploads.
    /// </summary>
    [JsonPropertyName("deployRepository")]
    public string? DeployRepository { get; set; }

    /// <summary>A copy that can be changed without affecting this one.</summary>
    public VirtualRepositorySettings Clone() => new() { Members = Members.ToList(), DeployRepository = DeployRepository };

    /// <summary>
    /// The problems that need no database: the member names, duplicates, the member count and a deploy repository
    /// that is not a member.
    /// </summary>
    public IReadOnlyList<string> ValidateShape()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (Members.Count > MaxMembers) problems.Add($"A virtual repository has at most {MaxMembers} members.");

        foreach (var member in Members)
        {
            if (!RepositoryNames.IsValid(member, out _))
            {
                problems.Add($"'{member}' is not a valid repository name.");
            }
            else if (!seen.Add(member))
            {
                problems.Add($"'{member}' is listed twice.");
            }
        }

        if (!string.IsNullOrEmpty(DeployRepository) && !seen.Contains(DeployRepository!))
        {
            problems.Add($"The deploy repository '{DeployRepository}' must be one of the members.");
        }

        return problems;
    }
}

/// <summary>
/// Reads and writes a virtual repository's <see cref="Repository.Settings"/> JSON.
/// </summary>
public static class VirtualSettingsCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The JSON stored for <paramref name="settings"/>.</summary>
    public static string Serialize(VirtualRepositorySettings settings)
        => JsonSerializer.Serialize(settings ?? throw new ArgumentNullException(nameof(settings)), Options);

    /// <summary>The settings in <paramref name="json"/>; an empty or unreadable value reads as no members.</summary>
    public static VirtualRepositorySettings Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new VirtualRepositorySettings();

        try
        {
            var settings = JsonSerializer.Deserialize<VirtualRepositorySettings>(json!, Options) ?? new VirtualRepositorySettings();
            settings.Members ??= new List<string>();
            settings.Members = settings.Members.Where(m => !string.IsNullOrEmpty(m)).ToList();

            return settings;
        }
        catch (JsonException)
        {
            return new VirtualRepositorySettings();
        }
    }
}
