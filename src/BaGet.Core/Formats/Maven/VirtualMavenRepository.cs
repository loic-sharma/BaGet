using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Core;

/// <summary>
/// <see cref="IMavenRepository"/> of a <see cref="RepositoryKind.Virtual"/> Maven repository: one repository made of its members,
/// local or remote, in resolution order (<see cref="VirtualRepositoryReader"/>: only the members the caller may read, each asked in
/// its own scope, so its storage, upstream and rules apply; a member that fails is logged and skipped).
/// </summary>
/// <remarks>
/// <para><b>Files and their checksums</b> come from the first member that has them, so a file and its <c>.sha1</c> always agree.</para>
/// <para><b>Metadata.</b> Artifact-level <c>maven-metadata.xml</c> is merged: <c>versions</c> is the union (ordered as Maven orders
/// versions), <c>latest</c> and <c>release</c> are the first member's own that the merged list contains (so they follow the member
/// order, not the highest version; when no member names one, the highest), <c>lastUpdated</c> the latest. The version-level
/// document (a snapshot's timestamped builds) is the first member's that has it, because the builds it names are that member's files.
/// A group-level document (plugin prefixes) is the union of the members' plugins, a prefix once. The checksums of metadata are those
/// of the merged document.</para>
/// <para><b>Pages and search</b> merge the members' artifacts by <c>group:artifact</c> (the first member's entry wins a version).
/// <b>Writes</b> go to the deploy repository, with that repository's rules and the caller's rights there: the HTTP routes and pages are
/// moved to it before they reach this class (<c>RepositoryResolutionMiddleware</c>, <c>RepositoryPageGate</c>); a caller of the service
/// gets the same through this class. Without a deploy repository they are refused (403).</para>
/// </remarks>
public sealed class VirtualMavenRepository : IMavenRepository
{
    // The most search hits read from one member for a page: a merged page of skip+take needs at most that many from each.
    internal const int MaxScanPerMember = 1000;

    private readonly IRepositoryContext _repository;
    private readonly VirtualRepositoryReader _reader;
    private readonly IVirtualRepositories _virtuals;

    public VirtualMavenRepository(IRepositoryContext repository, VirtualRepositoryReader reader, IVirtualRepositories virtuals)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _virtuals = virtuals ?? throw new ArgumentNullException(nameof(virtuals));
    }

    // ---- reads ----------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>A metadata path that can name a version or an artifact is the version when a member says so (its own resolution), else the artifact.</remarks>
    public async Task<MavenCoordinate?> ResolveAsync(MavenRequestPath path, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (path.Candidates.Count == 0) return null;
        if (path.Kind == MavenPathKind.File) return path.Candidates[0];

        // A single candidate (a path two folders deep) can still be a group's plugin list: a member that says so (null) makes the
        // document a group-level one, judged by the repository's Read only.
        var single = path.Candidates.Count == 1;
        if (single && (path.GroupOnly == null || path.Candidates[0].Version != null)) return path.Candidates[0];

        foreach (var member in await _reader.ReadableMembersAsync(null, cancellationToken).ConfigureAwait(false))
        {
            MavenCoordinate? resolved;
            try
            {
                resolved = await _reader.InMemberAsync(member, sp => sp.GetRequiredService<IMavenRepository>().ResolveAsync(path, cancellationToken)).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException and not MaintenanceException)
            {
                continue;
            }

            if (single && resolved == null) return null;

            // A member answers the artifact level when it knows nothing; only the first candidate is a positive answer.
            if (resolved != null && resolved == path.Candidates[0]) return resolved;
        }

        return path.Candidates[^1];
    }

    /// <inheritdoc/>
    public async Task<MavenDownload?> OpenAsync(MavenRequestPath path, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));

        if (path.Kind == MavenPathKind.File)
        {
            return await _reader.FirstAsync<MavenDownload>(
                path.Candidates[0].Name,
                sp => sp.GetRequiredService<IMavenRepository>().OpenAsync(path, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        return await OpenMetadataAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<MavenDownload?> HeadAsync(MavenRequestPath path, CancellationToken cancellationToken)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));

        if (path.Kind == MavenPathKind.File)
        {
            return await _reader.FirstAsync<MavenDownload>(
                path.Candidates[0].Name,
                sp => sp.GetRequiredService<IMavenRepository>().HeadAsync(path, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        // Metadata is small and merged: the same work as a GET.
        return await OpenMetadataAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MavenDownload?> OpenMetadataAsync(MavenRequestPath path, CancellationToken cancellationToken)
    {
        var plain = path.Plain;
        var coordinate = await ResolveAsync(path, cancellationToken).ConfigureAwait(false);

        // Each readable member's own document, in member order (a member that has none or fails contributes nothing).
        var documents = new List<(XElement Root, DateTime Modified)>();
        foreach (var member in await _reader.ReadableMembersAsync(coordinate?.Name, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var found = await _reader.InMemberAsync(member, async sp =>
                {
                    using var download = await sp.GetRequiredService<IMavenRepository>().OpenAsync(plain, cancellationToken).ConfigureAwait(false);
                    if (download == null) return ((XElement, DateTime)?)null;

                    using var buffer = new MemoryStream();
                    await download.Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                    buffer.Position = 0;
                    using var reader = XmlReader.Create(buffer, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

                    return (XDocument.Load(reader).Root!, download.LastModified);
                }).ConfigureAwait(false);

                if (found != null) documents.Add(found.Value);
            }
            catch (Exception e) when (e is not OperationCanceledException and not MaintenanceException)
            {
                // A member that cannot answer, or answers with something that is not metadata, is skipped like any failing member.
            }
        }

        if (documents.Count == 0) return null;

        var modified = documents.Max(d => d.Modified);
        var roots = documents.Select(d => d.Root).ToList();

        // What a document is follows from its content, not from the path (a path can be read several ways): plugin prefixes, a
        // version's (a snapshot's builds), or an artifact's list of versions.
        string xml;
        if (roots.Any(r => r.Element("plugins") != null))
        {
            xml = MergePlugins(roots);
        }
        else if (roots[0].Element("version") != null)
        {
            // The builds a snapshot document names are files of the member that wrote it: the first member's document, as is.
            xml = MetadataDocument(roots[0].ToString());
        }
        else
        {
            var group = roots.Select(r => r.Element("groupId")?.Value.Trim()).FirstOrDefault(g => !string.IsNullOrEmpty(g)) ?? coordinate?.GroupId ?? string.Empty;
            var artifact = roots.Select(r => r.Element("artifactId")?.Value.Trim()).FirstOrDefault(a => !string.IsNullOrEmpty(a)) ?? coordinate?.ArtifactId ?? string.Empty;
            xml = MergeVersions(new MavenCoordinate(group, artifact, null), roots, modified);
        }

        var content = System.Text.Encoding.UTF8.GetBytes(xml);
        if (path.ChecksumAlgorithm != null)
        {
            var text = System.Text.Encoding.ASCII.GetBytes(RemoteMavenRepository.Hex(path.ChecksumAlgorithm, content));

            return new MavenDownload(new MemoryStream(text, writable: false), text.Length, "text/plain", path.FileName, modified);
        }

        return new MavenDownload(new MemoryStream(content, writable: false), content.Length, "application/xml", path.FileName, modified)
        {
            Sha1 = RemoteMavenRepository.Hex("sha1", content),
            Md5 = RemoteMavenRepository.Hex("md5", content),
            Sha256 = RemoteMavenRepository.Hex("sha256", content),
        };
    }

    private static string MetadataDocument(string root) => new XDeclaration("1.0", "UTF-8", null) + Environment.NewLine + root + Environment.NewLine;

    /// <summary>The artifact-level document of the members' documents: see <see cref="VirtualMavenRepository"/>.</summary>
    internal static string MergeVersions(MavenCoordinate artifact, IReadOnlyList<XElement> documents, DateTime lastUpdated)
    {
        var versions = new List<string>();
        string? latest = null, release = null;
        foreach (var document in documents)
        {
            var versioning = document.Element("versioning");
            versions.AddRange(versioning?.Element("versions")?.Elements("version").Select(v => v.Value.Trim()).Where(v => v.Length > 0) ?? Enumerable.Empty<string>());
        }

        var merged = new HashSet<string>(versions, StringComparer.Ordinal);
        foreach (var document in documents)
        {
            var versioning = document.Element("versioning");
            var documentLatest = versioning?.Element("latest")?.Value.Trim();
            var documentRelease = versioning?.Element("release")?.Value.Trim();
            if (latest == null && documentLatest != null && merged.Contains(documentLatest)) latest = documentLatest;
            if (release == null && documentRelease != null && merged.Contains(documentRelease)) release = documentRelease;
        }

        return MavenMetadata.ForArtifact(artifact.GroupId, artifact.ArtifactId, merged, lastUpdated, latest, release);
    }

    // Plugin prefixes: the first member's entry for a prefix and artifact wins.
    private static string MergePlugins(IReadOnlyList<XElement> documents)
    {
        var plugins = new List<XElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plugin in documents.SelectMany(d => d.Element("plugins")?.Elements("plugin") ?? Enumerable.Empty<XElement>()))
        {
            var key = (plugin.Element("prefix")?.Value.Trim() ?? string.Empty) + "|" + (plugin.Element("artifactId")?.Value.Trim() ?? string.Empty);
            if (seen.Add(key)) plugins.Add(new XElement(plugin));
        }

        return MetadataDocument(new XElement("metadata", new XElement("plugins", plugins)).ToString());
    }

    /// <inheritdoc/>
    public async Task<MavenSearchPage> SearchAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead, CancellationToken cancellationToken)
    {
        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 0, 250);
        var window = Math.Min(skip + take, MaxScanPerMember);

        var byName = new SortedDictionary<string, MavenSearchHit>(StringComparer.Ordinal);
        var complete = true;
        var upperBound = 0;

        foreach (var member in await _reader.ReadableMembersAsync(null, cancellationToken).ConfigureAwait(false))
        {
            MavenSearchPage? page;
            try
            {
                var asked = member;
                page = await _reader.InMemberAsync<MavenSearchPage?>(member, async sp => await sp.GetRequiredService<IMavenRepository>().SearchAsync(
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
            foreach (var hit in page.Hits) byName.TryAdd(hit.CoordinateName, hit);
        }

        // Exact while every member was read completely; else the most the members could hold together.
        var total = complete ? byName.Count : Math.Max(byName.Count, upperBound);

        return new MavenSearchPage(total, byName.Values.Skip(skip).Take(take).ToList());
    }

    /// <inheritdoc/>
    public async Task<MavenArtifactInfo?> GetArtifactAsync(string groupId, string artifactId, CancellationToken cancellationToken)
    {
        var members = await _reader.AllAsync(
            MavenNames.ArtifactName(groupId, artifactId),
            async sp => (await sp.GetRequiredService<IMavenRepository>().GetArtifactAsync(groupId, artifactId, cancellationToken).ConfigureAwait(false))!,
            cancellationToken).ConfigureAwait(false);
        if (members.Count == 0) return null;

        var versions = new Dictionary<string, MavenVersionSummary>(StringComparer.Ordinal);
        foreach (var version in members.SelectMany(m => m.Value.Versions)) versions.TryAdd(version.Version, version);

        return new MavenArtifactInfo(
            groupId,
            artifactId,
            members.Min(m => m.Value.Created),
            members.Max(m => m.Value.Modified),
            versions.Values.OrderByDescending(v => v.Version, Comparer<string>.Create(MavenNames.CompareVersions)).ToList());
    }

    /// <inheritdoc/>
    public Task<MavenVersionDetail?> GetVersionAsync(string groupId, string artifactId, string version, CancellationToken cancellationToken)
        => _reader.FirstAsync<MavenVersionDetail>(
            MavenNames.ArtifactName(groupId, artifactId),
            sp => sp.GetRequiredService<IMavenRepository>().GetVersionAsync(groupId, artifactId, version, cancellationToken),
            cancellationToken);

    /// <inheritdoc/>
    public Task<MavenRepositorySettings> GetSettingsAsync(CancellationToken cancellationToken)
        => Task.FromResult(new MavenRepositorySettings { Policy = MavenPolicy.Both });

    /// <inheritdoc/>
    public async Task<ResolutionOutcome> ProbeAsync(string groupId, string artifactId, string version, CancellationToken cancellationToken)
    {
        var worst = ResolutionOutcome.NotFound;
        foreach (var member in await _reader.ReadableMembersAsync(MavenNames.ArtifactName(groupId, artifactId), cancellationToken).ConfigureAwait(false))
        {
            var outcome = await _reader.InMemberAsync(member, sp => sp.GetRequiredService<IMavenRepository>().ProbeAsync(groupId, artifactId, version, cancellationToken)).ConfigureAwait(false);
            if (outcome is ResolutionOutcome.Stored or ResolutionOutcome.WouldFetch) return outcome;
            if (outcome is ResolutionOutcome.Unavailable or ResolutionOutcome.Offline) worst = outcome;
        }

        return worst;
    }

    // ---- writes: the deploy repository ----------------------------------------------------------------------------------

    private async Task<MavenResult> OnDeployAsync(BaGetPrincipal? caller, Func<IMavenRepository, Task<MavenResult>> write, CancellationToken cancellationToken)
    {
        if (caller == null) return MavenResult.Fail(MavenStatus.Unauthorized, "Authentication is required.");

        var target = await _virtuals.DeployTargetAsync(_repository.Current, cancellationToken).ConfigureAwait(false);
        if (target == null) return MavenResult.Fail(MavenStatus.Forbidden, "This virtual repository has no deploy repository, so it takes no changes.");

        return await _reader.InMemberAsync(target, sp => write(sp.GetRequiredService<IMavenRepository>())).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<MavenResult> PutAsync(BaGetPrincipal? caller, MavenRequestPath path, Stream content, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.PutAsync(caller, path, content, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<MavenResult> DeployAsync(BaGetPrincipal? caller, MavenDeployment deployment, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.DeployAsync(caller, deployment, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<MavenResult> DeleteFileAsync(BaGetPrincipal? caller, MavenCoordinate version, string fileName, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.DeleteFileAsync(caller, version, fileName, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<MavenResult> DeleteVersionAsync(BaGetPrincipal? caller, MavenCoordinate version, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.DeleteVersionAsync(caller, version, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<MavenResult> DeleteArtifactAsync(BaGetPrincipal? caller, string groupId, string artifactId, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.DeleteArtifactAsync(caller, groupId, artifactId, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<MavenResult> CleanupSnapshotsAsync(BaGetPrincipal? caller, MavenCoordinate version, int keep, CancellationToken cancellationToken)
        => OnDeployAsync(caller, r => r.CleanupSnapshotsAsync(caller, version, keep, cancellationToken), cancellationToken);
}

/// <summary>
/// The <see cref="IMavenRepository"/> everything resolves: it forwards to the repository that fits the kind of the request's
/// repository (<see cref="MavenRepositoryService"/> for a local one, <see cref="RemoteMavenRepository"/>, <see cref="VirtualMavenRepository"/>),
/// so the routes and pages never ask which. Resolved per call, because a write to a virtual repository continues in its deploy repository.
/// </summary>
public sealed class RepositoryMavenRepository : IMavenRepository
{
    private readonly IRepositoryContext _repository;
    private readonly IServiceProvider _services;

    public RepositoryMavenRepository(IRepositoryContext repository, IServiceProvider services)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    private IMavenRepository Inner => _repository.Current.Kind switch
    {
        RepositoryKind.Remote => _services.GetRequiredService<RemoteMavenRepository>(),
        RepositoryKind.Virtual => _services.GetRequiredService<VirtualMavenRepository>(),
        _ => _services.GetRequiredService<MavenRepositoryService>(),
    };

    /// <inheritdoc/>
    public Task<MavenCoordinate?> ResolveAsync(MavenRequestPath path, CancellationToken cancellationToken) => Inner.ResolveAsync(path, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenDownload?> OpenAsync(MavenRequestPath path, CancellationToken cancellationToken) => Inner.OpenAsync(path, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenDownload?> HeadAsync(MavenRequestPath path, CancellationToken cancellationToken) => Inner.HeadAsync(path, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenResult> PutAsync(BaGetPrincipal? caller, MavenRequestPath path, Stream content, CancellationToken cancellationToken) => Inner.PutAsync(caller, path, content, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenResult> DeployAsync(BaGetPrincipal? caller, MavenDeployment deployment, CancellationToken cancellationToken) => Inner.DeployAsync(caller, deployment, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenResult> DeleteFileAsync(BaGetPrincipal? caller, MavenCoordinate version, string fileName, CancellationToken cancellationToken) => Inner.DeleteFileAsync(caller, version, fileName, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenResult> DeleteVersionAsync(BaGetPrincipal? caller, MavenCoordinate version, CancellationToken cancellationToken) => Inner.DeleteVersionAsync(caller, version, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenResult> DeleteArtifactAsync(BaGetPrincipal? caller, string groupId, string artifactId, CancellationToken cancellationToken) => Inner.DeleteArtifactAsync(caller, groupId, artifactId, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenResult> CleanupSnapshotsAsync(BaGetPrincipal? caller, MavenCoordinate version, int keep, CancellationToken cancellationToken) => Inner.CleanupSnapshotsAsync(caller, version, keep, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenSearchPage> SearchAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead, CancellationToken cancellationToken)
        => Inner.SearchAsync(text, skip, take, canRead, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenArtifactInfo?> GetArtifactAsync(string groupId, string artifactId, CancellationToken cancellationToken) => Inner.GetArtifactAsync(groupId, artifactId, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenVersionDetail?> GetVersionAsync(string groupId, string artifactId, string version, CancellationToken cancellationToken) => Inner.GetVersionAsync(groupId, artifactId, version, cancellationToken);

    /// <inheritdoc/>
    public Task<MavenRepositorySettings> GetSettingsAsync(CancellationToken cancellationToken) => Inner.GetSettingsAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<ResolutionOutcome> ProbeAsync(string groupId, string artifactId, string version, CancellationToken cancellationToken) => Inner.ProbeAsync(groupId, artifactId, version, cancellationToken);
}
