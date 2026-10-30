using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Core;

/// <summary>
/// <see cref="INpmRegistry"/> of a <see cref="RepositoryKind.Virtual"/> npm repository: one registry made of its members, local or
/// remote, in resolution order (<see cref="VirtualRepositoryReader"/>: only the members the caller may read, each asked in its own
/// scope, so its storage, upstream and rules apply; a member that fails is logged and skipped).
/// </summary>
/// <remarks>
/// <para><b>Packument.</b> The members' packuments are merged: <c>versions</c> is the union, a version coming from the first
/// member that has it (its <c>dist.tarball</c> already points into the virtual repository: addresses come from the request);
/// <c>dist-tags</c> take, per tag, the first member that has the tag, so <c>latest</c> follows the member order, not the highest
/// version; <c>time</c> is the union with the earliest <c>created</c> and the latest <c>modified</c>; the other fields come from
/// the first member and are completed from the later ones. A package no member has is absent.</para>
/// <para><b>Tarball</b> from the first member that has it. <b>Search</b> merges the members' listings by name (the first member's
/// entry wins), ordered by name; remote members contribute the packages they stored.</para>
/// <para><b>Writes</b> go to the deploy repository, with that repository's rules and the caller's rights there: the HTTP routes and
/// pages are moved to it before they reach this class (<c>RepositoryResolutionMiddleware</c>, <c>RepositoryPageGate</c>); a caller of
/// the service gets the same through this class. Without a deploy repository they are refused (403).</para>
/// </remarks>
public sealed class VirtualNpmRegistry : INpmRegistry
{
    // The most search hits read from one member for a page: a merged page of skip+take needs at most that many from each.
    internal const int MaxScanPerMember = 1000;

    private readonly IRepositoryContext _repository;
    private readonly VirtualRepositoryReader _reader;
    private readonly IVirtualRepositories _virtuals;

    public VirtualNpmRegistry(IRepositoryContext repository, VirtualRepositoryReader reader, IVirtualRepositories virtuals)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _virtuals = virtuals ?? throw new ArgumentNullException(nameof(virtuals));
    }

    // ---- reads ----------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<JsonObject?> GetPackumentAsync(string name, bool abbreviated, CancellationToken cancellationToken)
    {
        var members = await _reader.AllAsync(
            name,
            async sp => (await sp.GetRequiredService<INpmRegistry>().GetPackumentAsync(name, abbreviated, cancellationToken).ConfigureAwait(false))!,
            cancellationToken).ConfigureAwait(false);

        return members.Count == 0 ? null : MergePackuments(name, members.Select(m => m.Value).ToList(), abbreviated);
    }

    /// <summary>Merges member packuments (in resolution order) as described on <see cref="VirtualNpmRegistry"/>.</summary>
    internal static JsonObject MergePackuments(string name, IReadOnlyList<JsonObject> documents, bool abbreviated)
    {
        var result = new JsonObject();
        var versions = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var comparable = new HashSet<string>(StringComparer.Ordinal);
        var times = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        DateTime? created = null, modified = null;
        var revisions = new StringBuilder();

        foreach (var document in documents)
        {
            var time = document["time"] as JsonObject;
            if (document["versions"] is JsonObject own)
            {
                foreach (var pair in own)
                {
                    if (pair.Value == null || !comparable.Add(NpmNames.Comparable(pair.Key))) continue;

                    versions[pair.Key] = pair.Value.DeepClone();
                    if (time?[pair.Key] is JsonNode at) times[pair.Key] = at.DeepClone();
                }
            }

            foreach (var pair in (document["dist-tags"] as JsonObject) ?? new JsonObject())
            {
                if (pair.Value is JsonValue v && v.TryGetValue<string>(out var target) && !tags.ContainsKey(pair.Key)) tags[pair.Key] = target;
            }

            created = Earlier(created, Time(time?["created"]));
            modified = Later(modified, Time(time?["modified"] ?? document["modified"]));
            revisions.Append(document["_rev"] is JsonValue rev && rev.TryGetValue<string>(out var revision) ? revision : string.Empty).Append('|');

            // The descriptive fields (description, readme, license...): the first member's, completed from the later ones.
            foreach (var pair in document)
            {
                if (pair.Key is "versions" or "dist-tags" or "time" or "_rev" or "modified" or "name" or "_id" || pair.Value == null || result.ContainsKey(pair.Key)) continue;

                result[pair.Key] = pair.Value.DeepClone();
            }
        }

        // A tag only points at a version the merged document has (a member's tag always does; an upstream may be inconsistent).
        foreach (var tag in tags.Where(t => !versions.ContainsKey(t.Value)).ToList()) tags.Remove(tag.Key);
        if (!tags.ContainsKey("latest"))
        {
            var highest = NpmNames.Highest(versions.Keys);
            if (highest != null) tags["latest"] = highest;
        }

        var versionsNode = new JsonObject();
        var timeNode = new JsonObject();
        foreach (var version in versions.Keys.OrderBy(v => v, new NpmVersionOrder()))
        {
            versionsNode[version] = versions[version];
            if (times.TryGetValue(version, out var at)) timeNode[version] = at;
        }

        var tagsNode = new JsonObject();
        foreach (var tag in tags.OrderBy(t => t.Key, StringComparer.Ordinal)) tagsNode[tag.Key] = tag.Value;

        var stamp = modified ?? DateTime.UnixEpoch;
        result["name"] = name;
        result["dist-tags"] = tagsNode;
        result["versions"] = versionsNode;

        if (abbreviated)
        {
            result["modified"] = Iso(stamp);
        }
        else
        {
            result["_id"] = name;
            result["_rev"] = $"{versions.Count}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revisions.ToString())), 0, 8).ToLowerInvariant()}";
            timeNode["created"] = Iso(created ?? stamp);
            timeNode["modified"] = Iso(stamp);
            result["time"] = timeNode;
        }

        return result;
    }

    private static DateTime? Time(JsonNode? node)
        => node is JsonValue v && v.TryGetValue<string>(out var text)
            && DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static DateTime? Earlier(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : (a < b ? a : b);

    private static DateTime? Later(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : (a > b ? a : b);

    private static string Iso(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public async Task<NpmPackageInfo?> GetPackageAsync(string name, CancellationToken cancellationToken)
    {
        var members = await _reader.AllAsync(
            name,
            async sp => (await sp.GetRequiredService<INpmRegistry>().GetPackageAsync(name, cancellationToken).ConfigureAwait(false))!,
            cancellationToken).ConfigureAwait(false);
        if (members.Count == 0) return null;

        var versions = new Dictionary<string, NpmVersionInfo>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var info in members.Select(m => m.Value))
        {
            foreach (var version in info.Versions)
            {
                if (seen.Add(NpmNames.Comparable(version.Version))) versions[version.Version] = version;
            }

            foreach (var tag in info.DistTags)
            {
                if (!tags.ContainsKey(tag.Key)) tags[tag.Key] = tag.Value;
            }
        }

        foreach (var tag in tags.Where(t => !versions.ContainsKey(t.Value)).ToList()) tags.Remove(tag.Key);
        if (!tags.ContainsKey("latest") && NpmNames.Highest(versions.Keys) is { } highest) tags["latest"] = highest;

        return new NpmPackageInfo(
            name,
            members.Min(m => m.Value.Created),
            members.Max(m => m.Value.Modified),
            tags,
            versions.Values.OrderBy(v => v.Version, new NpmVersionOrder()).ToList());
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>?> GetDistTagsAsync(string name, CancellationToken cancellationToken)
        => (await GetPackageAsync(name, cancellationToken).ConfigureAwait(false))?.DistTags;

    /// <inheritdoc/>
    public Task<NpmTarball?> OpenTarballAsync(string name, string file, CancellationToken cancellationToken)
        => _reader.FirstAsync<NpmTarball>(name, sp => sp.GetRequiredService<INpmRegistry>().OpenTarballAsync(name, file, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public async Task<NpmSearchPage> SearchAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead = null, CancellationToken cancellationToken = default)
    {
        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 0, 250);
        var window = Math.Min(skip + take, MaxScanPerMember);

        var byName = new SortedDictionary<string, NpmSearchHit>(StringComparer.Ordinal);
        var complete = true;
        var upperBound = 0;

        foreach (var member in await _reader.ReadableMembersAsync(null, cancellationToken).ConfigureAwait(false))
        {
            NpmSearchPage? page;
            try
            {
                var asked = member;
                page = await _reader.InMemberAsync<NpmSearchPage?>(member, async sp => await sp.GetRequiredService<INpmRegistry>().SearchAsync(
                    text,
                    0,
                    window,
                    async (name, token) => await _reader.CanReadInMemberAsync(asked, name, token).ConfigureAwait(false)
                        && (canRead == null || await canRead(name, token).ConfigureAwait(false)),
                    cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException and not MaintenanceException)
            {
                complete = false;
                continue;
            }

            if (page == null) continue;

            upperBound += page.Total;
            if (page.Total > page.Hits.Count) complete = false;
            foreach (var hit in page.Hits) byName.TryAdd(hit.Name, hit);
        }

        // Exact while every member was read completely; else the most the members could hold together.
        var total = complete ? byName.Count : Math.Max(byName.Count, upperBound);

        return new NpmSearchPage(total, byName.Values.Skip(skip).Take(take).ToList());
    }

    /// <inheritdoc/>
    public async Task<ResolutionOutcome> ProbeAsync(string name, string version, CancellationToken cancellationToken)
    {
        var worst = ResolutionOutcome.NotFound;
        foreach (var member in await _reader.ReadableMembersAsync(name, cancellationToken).ConfigureAwait(false))
        {
            var outcome = await _reader.InMemberAsync(member, sp => sp.GetRequiredService<INpmRegistry>().ProbeAsync(name, version, cancellationToken)).ConfigureAwait(false);
            if (outcome is ResolutionOutcome.Stored or ResolutionOutcome.WouldFetch) return outcome;
            if (outcome is ResolutionOutcome.Unavailable or ResolutionOutcome.Offline) worst = outcome;
        }

        return worst;
    }

    // ---- writes: the deploy repository ----------------------------------------------------------------------------------

    private async Task<NpmResult> OnDeployAsync(BaGetPrincipal? caller, Func<INpmRegistry, Task<NpmResult>> write, CancellationToken cancellationToken)
    {
        if (caller == null) return NpmResult.Fail(NpmStatus.Unauthorized, "Authentication is required.");

        var target = await _virtuals.DeployTargetAsync(_repository.Current, cancellationToken).ConfigureAwait(false);
        if (target == null) return NpmResult.Fail(NpmStatus.Forbidden, "This virtual repository has no deploy repository, so it takes no changes.");

        return await _reader.InMemberAsync(target, sp => write(sp.GetRequiredService<INpmRegistry>())).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<NpmResult> PublishAsync(BaGetPrincipal? caller, NpmPublication publication, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.PublishAsync(caller, publication, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> PublishTarballAsync(BaGetPrincipal? caller, Stream tarball, string tag, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.PublishTarballAsync(caller, tarball, tag, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> SetDistTagAsync(BaGetPrincipal? caller, string name, string tag, string version, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.SetDistTagAsync(caller, name, tag, version, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> RemoveDistTagAsync(BaGetPrincipal? caller, string name, string tag, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.RemoveDistTagAsync(caller, name, tag, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> DeprecateAsync(BaGetPrincipal? caller, string name, IReadOnlyDictionary<string, string?> messages, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.DeprecateAsync(caller, name, messages, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> UnpublishVersionsAsync(BaGetPrincipal? caller, string name, IReadOnlyCollection<string> versions, IReadOnlyDictionary<string, string>? newTags, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.UnpublishVersionsAsync(caller, name, versions, newTags, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> UnpublishPackageAsync(BaGetPrincipal? caller, string name, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.UnpublishPackageAsync(caller, name, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> DeleteTarballAsync(BaGetPrincipal? caller, string name, string file, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.DeleteTarballAsync(caller, name, file, cancellationToken), cancellationToken);
}

/// <summary>
/// The <see cref="INpmRegistry"/> everything resolves: it forwards to the registry that fits the kind of the request's
/// repository (<see cref="NpmRegistryService"/> for a local one, <see cref="RemoteNpmRegistry"/>, <see cref="VirtualNpmRegistry"/>), so
/// the routes and pages never ask which.
/// </summary>
public sealed class RepositoryNpmRegistry : INpmRegistry
{
    private readonly IRepositoryContext _repository;
    private readonly IServiceProvider _services;

    public RepositoryNpmRegistry(IRepositoryContext repository, IServiceProvider services)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    // Resolved per call: the repository of the scope can move (a write to a virtual repository continues in its deploy repository).
    private INpmRegistry Inner => _repository.Current.Kind switch
    {
        RepositoryKind.Remote => _services.GetRequiredService<RemoteNpmRegistry>(),
        RepositoryKind.Virtual => _services.GetRequiredService<VirtualNpmRegistry>(),
        _ => _services.GetRequiredService<NpmRegistryService>(),
    };

    /// <inheritdoc/>
    public Task<NpmPackageInfo?> GetPackageAsync(string name, CancellationToken cancellationToken) => Inner.GetPackageAsync(name, cancellationToken);

    /// <inheritdoc/>
    public Task<JsonObject?> GetPackumentAsync(string name, bool abbreviated, CancellationToken cancellationToken) => Inner.GetPackumentAsync(name, abbreviated, cancellationToken);

    /// <inheritdoc/>
    public Task<NpmTarball?> OpenTarballAsync(string name, string file, CancellationToken cancellationToken) => Inner.OpenTarballAsync(name, file, cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> PublishAsync(BaGetPrincipal? caller, NpmPublication publication, CancellationToken cancellationToken) => Inner.PublishAsync(caller, publication, cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> PublishTarballAsync(BaGetPrincipal? caller, Stream tarball, string tag, CancellationToken cancellationToken) => Inner.PublishTarballAsync(caller, tarball, tag, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, string>?> GetDistTagsAsync(string name, CancellationToken cancellationToken) => Inner.GetDistTagsAsync(name, cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> SetDistTagAsync(BaGetPrincipal? caller, string name, string tag, string version, CancellationToken cancellationToken) => Inner.SetDistTagAsync(caller, name, tag, version, cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> RemoveDistTagAsync(BaGetPrincipal? caller, string name, string tag, CancellationToken cancellationToken) => Inner.RemoveDistTagAsync(caller, name, tag, cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> DeprecateAsync(BaGetPrincipal? caller, string name, IReadOnlyDictionary<string, string?> messages, CancellationToken cancellationToken) => Inner.DeprecateAsync(caller, name, messages, cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> UnpublishVersionsAsync(BaGetPrincipal? caller, string name, IReadOnlyCollection<string> versions, IReadOnlyDictionary<string, string>? newTags, CancellationToken cancellationToken)
        => Inner.UnpublishVersionsAsync(caller, name, versions, newTags, cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> UnpublishPackageAsync(BaGetPrincipal? caller, string name, CancellationToken cancellationToken) => Inner.UnpublishPackageAsync(caller, name, cancellationToken);

    /// <inheritdoc/>
    public Task<NpmResult> DeleteTarballAsync(BaGetPrincipal? caller, string name, string file, CancellationToken cancellationToken) => Inner.DeleteTarballAsync(caller, name, file, cancellationToken);

    /// <inheritdoc/>
    public Task<NpmSearchPage> SearchAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead = null, CancellationToken cancellationToken = default)
        => Inner.SearchAsync(text, skip, take, canRead, cancellationToken);

    /// <inheritdoc/>
    public Task<ResolutionOutcome> ProbeAsync(string name, string version, CancellationToken cancellationToken) => Inner.ProbeAsync(name, version, cancellationToken);
}
