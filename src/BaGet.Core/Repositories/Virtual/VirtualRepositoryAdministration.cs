using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>One member of a virtual repository as the administration page lists it.</summary>
/// <param name="Name">The member's name, as stored.</param>
/// <param name="Repository">The member repository, or null when no repository has that name any more.</param>
public sealed record VirtualMemberDetail(string Name, RepositoryInfo? Repository)
{
    /// <summary>Whether a repository of that name exists.</summary>
    public bool Exists => Repository != null;
}

/// <summary>A virtual repository as the administration pages edit it.</summary>
/// <param name="Repository">The virtual repository.</param>
/// <param name="Members">Its direct members in resolution order.</param>
/// <param name="DeployRepository">The name of the member that receives deployments, if any.</param>
/// <param name="DeployTarget">The local repository a push really goes to (the deploy repository, or where a virtual deploy repository deploys), or null.</param>
/// <param name="EffectiveMembers">The local and remote repositories that answer, in order, nested virtual members expanded.</param>
public sealed record VirtualRepositoryDetail(
    RepositoryInfo Repository,
    IReadOnlyList<VirtualMemberDetail> Members,
    string? DeployRepository,
    RepositoryInfo? DeployTarget,
    IReadOnlyList<RepositoryInfo> EffectiveMembers);

/// <summary>What asking one member about a package version found.</summary>
public enum ResolutionOutcome
{
    /// <summary>The member stores the version.</summary>
    Stored,

    /// <summary>A remote member that does not store it yet but whose upstream has it (it would be fetched).</summary>
    WouldFetch,

    /// <summary>The member does not have it.</summary>
    NotFound,

    /// <summary>A remote member whose upstream could not be asked.</summary>
    Unavailable,

    /// <summary>Not asked: an earlier member already answers (a member's upstream is not contacted then).</summary>
    NotAsked,

    /// <summary>The member does not store it and its upstream is in offline mode, so a request would not ask it.</summary>
    Offline,
}

/// <summary>One member's part in <see cref="ResolutionPreview"/>.</summary>
/// <param name="Member">The member asked.</param>
/// <param name="Outcome">What it found.</param>
public sealed record ResolutionStep(RepositoryInfo Member, ResolutionOutcome Outcome);

/// <summary>Where a package version would come from, member by member.</summary>
/// <param name="PackageId">The package id asked about (<c>group:artifact</c> for Maven).</param>
/// <param name="Version">The version asked about, as written for the format (a NuGet or npm version in its normalized form; a Maven version as typed).</param>
/// <param name="Steps">The effective members in resolution order and what each found.</param>
/// <param name="Answers">The member that answers (the first that stores the version or whose upstream has it), or null when none.</param>
public sealed record ResolutionPreview(string PackageId, string Version, IReadOnlyList<ResolutionStep> Steps, RepositoryInfo? Answers);

/// <summary>
/// Creates, edits and explains virtual repositories. What the administration pages use; writes go through the database
/// context, so the <see cref="MaintenanceGate"/> refuses them while it is closed. The access model is not consulted:
/// the pages are administrators' tools (read access to a virtual repository's members is judged per request, see
/// <see cref="VirtualRepositoryReader"/>).
/// </summary>
public interface IVirtualRepositoryAdministration
{
    /// <summary>A virtual repository by exact name, or null when there is none (or it is not virtual).</summary>
    Task<VirtualRepositoryDetail?> FindAsync(string? name, CancellationToken cancellationToken);

    /// <summary>
    /// The repositories of the virtual repository's format that can be added to <paramref name="name"/> (every other
    /// repository whose own members do not lead back to it), ordered by name. For a repository not created yet pass null and its
    /// <paramref name="format"/>.
    /// </summary>
    Task<IReadOnlyList<RepositoryInfo>> CandidatesAsync(string? name, CancellationToken cancellationToken, RepositoryFormat format = RepositoryFormat.NuGet);

    /// <summary>The virtual repositories that list <paramref name="memberName"/> directly, ordered by name.</summary>
    Task<IReadOnlyList<string>> ReferencedByAsync(string memberName, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a virtual repository of <paramref name="format"/> (its members must be of that format: NuGet or npm). Audited.
    /// </summary>
    /// <exception cref="ArgumentException">The name or the settings are invalid; the message lists the problems.</exception>
    /// <exception cref="RepositoryExistsException">The name is taken.</exception>
    /// <exception cref="NotSupportedException">The configuration holds the default repository only.</exception>
    Task<RepositoryInfo> CreateAsync(string name, VirtualRepositorySettings settings, CancellationToken cancellationToken, string? actor = null, bool anonymousRead = true, RepositoryFormat format = RepositoryFormat.NuGet);

    /// <summary>
    /// Replaces the members and the deploy repository. Refused (nothing changes) for an unknown, remote-only or
    /// other-format member, for a deploy repository that is not a member or does not end in a local repository, and for a
    /// change that would close a cycle between virtual repositories. Audited.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such virtual repository.</exception>
    /// <exception cref="ArgumentException">The settings are invalid; the message lists the problems.</exception>
    Task UpdateAsync(string name, VirtualRepositorySettings settings, CancellationToken cancellationToken, string? actor = null);

    /// <summary>
    /// Where <paramref name="packageId"/> <paramref name="version"/> would come from: each effective member is asked in
    /// order whether it stores the version (a remote member that does not is asked whether its upstream has it, unless an
    /// earlier member already answers). Stores nothing and fetches nothing. For an npm repository the package id is the package
    /// name and the version is read as a full semantic version (its full string form).
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such virtual repository.</exception>
    Task<ResolutionPreview> PreviewAsync(string name, string packageId, NuGetVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="PreviewAsync(string, string, NuGetVersion, CancellationToken)"/> for a version written as text: a Maven version
    /// (<c>5.3.20.RELEASE</c>, <c>1.0-SNAPSHOT</c>) is not always a NuGet version, and the package id of a Maven repository is
    /// <c>group:artifact</c>. A NuGet or npm repository still needs a version that parses.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such virtual repository.</exception>
    /// <exception cref="ArgumentException">The version or the package id is not valid for the repository's format.</exception>
    Task<ResolutionPreview> PreviewAsync(string name, string packageId, string version, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IVirtualRepositoryAdministration"/> over the relational database.
/// </summary>
public sealed class VirtualRepositoryAdministration : IVirtualRepositoryAdministration
{
    private readonly IContext _context;
    private readonly IRepositoryService _repositories;
    private readonly IServiceScopeFactory _scopes;
    private readonly MaintenanceGate? _gate;
    private readonly SystemTime? _time;
    private readonly FormatRegistry _formats;

    public VirtualRepositoryAdministration(
        IContext context,
        IRepositoryService repositories,
        IServiceScopeFactory scopes,
        MaintenanceGate? gate = null,
        SystemTime? time = null,
        FormatRegistry? formats = null)
    {
        _formats = formats ?? FormatRegistry.Default;
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _gate = gate;
        _time = time;
    }

    private DateTime Now => _time?.UtcNow ?? DateTime.UtcNow;

    /// <inheritdoc/>
    public async Task<VirtualRepositoryDetail?> FindAsync(string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(name) || _context.Repositories == null) return null;

        var rows = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!rows.TryGetValue(name!, out var row) || row.Kind != RepositoryKind.Virtual) return null;

        var settings = VirtualSettingsCodec.Deserialize(row.Settings);
        var effective = new List<RepositoryInfo>();
        VirtualRepositories.Flatten(name!, rows, new HashSet<string>(StringComparer.Ordinal), effective, new HashSet<int>(), depth: 0);

        return new VirtualRepositoryDetail(
            row.ToInfo(),
            settings.Members.Select(m => new VirtualMemberDetail(m, rows.TryGetValue(m, out var member) ? member.ToInfo() : null)).ToList(),
            settings.DeployRepository,
            VirtualRepositories.ResolveDeploy(name!, rows),
            effective);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RepositoryInfo>> CandidatesAsync(string? name, CancellationToken cancellationToken, RepositoryFormat format = RepositoryFormat.NuGet)
    {
        if (_context.Repositories == null) return Array.Empty<RepositoryInfo>();

        var rows = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (name != null && rows.TryGetValue(name, out var self)) format = self.Format;

        return rows.Values
            .Where(r => r.Format == format && r.Name != name && !(name != null && r.Kind == RepositoryKind.Virtual && Reaches(r.Name!, name, rows)))
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => r.ToInfo())
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ReferencedByAsync(string memberName, CancellationToken cancellationToken)
    {
        if (_context.Repositories == null) return Array.Empty<string>();

        var rows = await LoadAsync(cancellationToken).ConfigureAwait(false);

        return rows.Values
            .Where(r => r.Kind == RepositoryKind.Virtual && VirtualSettingsCodec.Deserialize(r.Settings).Members.Contains(memberName))
            .Select(r => r.Name!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<RepositoryInfo> CreateAsync(string name, VirtualRepositorySettings settings, CancellationToken cancellationToken, string? actor = null, bool anonymousRead = true, RepositoryFormat format = RepositoryFormat.NuGet)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (!RepositoryNames.IsValid(name, out var error)) throw new ArgumentException(error, nameof(name));
        if (_formats.Find(format) is not { SupportsVirtual: true })
        {
            throw new NotSupportedException($"A virtual {format} repository is not available on this server.");
        }

        _gate?.ThrowIfClosed();

        if (!_repositories.SupportsMultipleRepositories)
        {
            throw new NotSupportedException(
                "Only the 'default' repository is available with this configuration; use a relational database (SQLite, SQL Server, PostgreSQL or MySQL) and a search type other than AzureSearch (whose index is not repository-aware) to create more.");
        }

        await _repositories.EnsureDefaultAsync(cancellationToken).ConfigureAwait(false);

        var rows = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (rows.ContainsKey(name)) throw new RepositoryExistsException(name);

        Validate(name, format, settings, rows);

        var row = new Repository
        {
            Name = name,
            Format = format,
            Kind = RepositoryKind.Virtual,
            Settings = VirtualSettingsCodec.Serialize(settings),
            AnonymousRead = anonymousRead,
            Created = Now,
        };
        _context.Repositories!.Add(row);
        AccessAdministration.Audit(_context, Now, actor, "repository.virtual.create", name, Describe(settings));

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException e) when (_context.IsUniqueConstraintViolationException(e))
        {
            throw new RepositoryExistsException(name);
        }

        return row.ToInfo();
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(string name, VirtualRepositorySettings settings, CancellationToken cancellationToken, string? actor = null)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        _gate?.ThrowIfClosed();

        var row = _context.Repositories == null
            ? null
            : await _context.Repositories.FirstOrDefaultAsync(r => r.Name == name && r.Kind == RepositoryKind.Virtual, cancellationToken).ConfigureAwait(false);
        if (row == null) throw new KeyNotFoundException($"There is no virtual repository named '{name}'.");

        var rows = await LoadAsync(cancellationToken).ConfigureAwait(false);
        Validate(name, row.Format, settings, rows);

        row.Settings = VirtualSettingsCodec.Serialize(settings);
        AccessAdministration.Audit(_context, Now, actor, "repository.virtual.update", name, Describe(settings));
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<ResolutionPreview> PreviewAsync(string name, string packageId, NuGetVersion version, CancellationToken cancellationToken)
        => PreviewAsync(name, packageId, version?.ToFullString() ?? throw new ArgumentNullException(nameof(version)), cancellationToken);

    /// <inheritdoc/>
    public async Task<ResolutionPreview> PreviewAsync(string name, string packageId, string version, CancellationToken cancellationToken)
    {
        if (version == null) throw new ArgumentNullException(nameof(version));

        var detail = await FindAsync(name, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"There is no virtual repository named '{name}'.");

        // NuGet and npm versions are semantic versions; a Maven version is any version folder name; a Docker "version" is a tag or a digest.
        NuGetVersion? parsed = null;
        string shown = version;
        string group = string.Empty, artifact = string.Empty;
        if (detail.Repository.Format == RepositoryFormat.Docker)
        {
            packageId = packageId.Trim().ToLowerInvariant();
            if (!OciNames.IsValidImage(packageId)) throw new ArgumentException($"{packageId} is not an image name: lower-case letters, digits, '.', '_', '-' in components separated by '/'.", nameof(packageId));
            if (!(OciNames.IsValidTag(version) || OciNames.IsValidDigest(version))) throw new ArgumentException($"{version} is not a tag or a sha256 digest.", nameof(version));
        }
        else if (detail.Repository.Format == RepositoryFormat.Maven)
        {
            if (!MavenNames.TryParseArtifactName(packageId, out group, out artifact)) throw new ArgumentException($"{packageId} is not an artifact: write it as group:artifact.", nameof(packageId));
            if (!MavenNames.IsValidVersion(version)) throw new ArgumentException($"{version} is not a valid version.", nameof(version));
        }
        else
        {
            if (!NuGetVersion.TryParse(version, out parsed)) throw new ArgumentException($"{version} is not a valid version.", nameof(version));

            shown = parsed.ToNormalizedString();
        }

        var steps = new List<ResolutionStep>();
        RepositoryInfo? answers = null;

        foreach (var member in detail.EffectiveMembers)
        {
            // Once a member answers the later ones are not asked: a real request stops there, and probing an upstream
            // is a request to it.
            var outcome = ResolutionOutcome.NotAsked;
            if (answers != null)
            {
                // Only the stored copy is looked at (cheap and local): it shows a member the answering one shadows.
                using var scope = _scopes.CreateRepositoryScope(member);
                if (await StoredAsync(scope.ServiceProvider, detail.Repository.Format, packageId, parsed, group, artifact, version, cancellationToken).ConfigureAwait(false))
                {
                    outcome = ResolutionOutcome.Stored;
                }
            }
            else
            {
                // The member's own package service decides, as it does for a real request: its stored copy first, then
                // its upstream (any member can have one: a remote repository, or the default repository's mirror).
                using var scope = _scopes.CreateRepositoryScope(member);
                try
                {
                    outcome = detail.Repository.Format switch
                    {
                        RepositoryFormat.Npm => await scope.ServiceProvider.GetRequiredService<INpmRegistry>()
                            .ProbeAsync(packageId.ToLowerInvariant(), parsed!.ToFullString(), cancellationToken).ConfigureAwait(false),
                        RepositoryFormat.Maven => await scope.ServiceProvider.GetRequiredService<IMavenRepository>()
                            .ProbeAsync(group, artifact, version, cancellationToken).ConfigureAwait(false),
                        RepositoryFormat.Docker => await scope.ServiceProvider.GetRequiredService<IOciRegistry>()
                            .ProbeAsync(packageId, version, cancellationToken).ConfigureAwait(false),
                        _ => await scope.ServiceProvider.GetRequiredService<PackageService>()
                            .ProbeAsync(packageId, parsed!, cancellationToken).ConfigureAwait(false),
                    };
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    outcome = ResolutionOutcome.Unavailable;
                }
            }

            if (answers == null && outcome is ResolutionOutcome.Stored or ResolutionOutcome.WouldFetch) answers = member;
            steps.Add(new ResolutionStep(member, outcome));
        }

        return new ResolutionPreview(packageId, shown, steps, answers);
    }

    // Whether the member's own storage holds the version (cheap and local).
    private static async Task<bool> StoredAsync(
        IServiceProvider services,
        RepositoryFormat format,
        string packageId,
        NuGetVersion? parsed,
        string group,
        string artifact,
        string version,
        CancellationToken cancellationToken)
    {
        if (format == RepositoryFormat.Docker)
        {
            // A Docker member's stored copy only: the probe of a remote member would ask its upstream, which a shadowed member must not.
            return await services.GetRequiredService<OciRegistryService>().ProbeAsync(packageId, version, cancellationToken).ConfigureAwait(false) == ResolutionOutcome.Stored;
        }

        if (format == RepositoryFormat.Maven)
        {
            // A Maven member's stored copy only: the probe of a remote member would ask its upstream, which a shadowed member must not.
            return await services.GetRequiredService<MavenRepositoryService>().HasVersionAsync(group, artifact, version, cancellationToken).ConfigureAwait(false);
        }

        if (format != RepositoryFormat.Npm)
        {
            return await services.GetRequiredService<IPackageDatabase>().ExistsAsync(packageId, parsed!, cancellationToken).ConfigureAwait(false);
        }

        // An npm member's stored copy only: the probe of a remote member would ask its upstream, which a shadowed member must not.
        return await services.GetRequiredService<NpmRegistryService>()
            .ProbeAsync(packageId.ToLowerInvariant(), parsed!.ToFullString(), cancellationToken).ConfigureAwait(false) == ResolutionOutcome.Stored;
    }

    private static object Describe(VirtualRepositorySettings settings)
        => new { members = settings.Members, deploy = settings.DeployRepository };

    private async Task<Dictionary<string, Repository>> LoadAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.Repositories!.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.Where(r => r.Name != null).ToDictionary(r => r.Name!, StringComparer.Ordinal);
    }

    // Whether the virtual repository 'from' lists 'target' directly or through nested virtual members.
    private static bool Reaches(string from, string target, IReadOnlyDictionary<string, Repository> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(from);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current) || !rows.TryGetValue(current, out var row) || row.Kind != RepositoryKind.Virtual) continue;

            foreach (var member in VirtualSettingsCodec.Deserialize(row.Settings).Members)
            {
                if (member == target) return true;
                pending.Push(member);
            }
        }

        return false;
    }

    // Throws ArgumentException listing every problem of 'settings' as the settings of the virtual repository 'name'.
    private static void Validate(string name, RepositoryFormat format, VirtualRepositorySettings settings, Dictionary<string, Repository> rows)
    {
        var problems = settings.ValidateShape().ToList();

        foreach (var member in settings.Members)
        {
            if (member == name)
            {
                problems.Add("A virtual repository cannot contain itself.");
            }
            else if (!rows.TryGetValue(member, out var row))
            {
                problems.Add($"There is no repository named '{member}'.");
            }
            else if (row.Format != format)
            {
                problems.Add($"'{member}' is not a {format} repository.");
            }
            else if (row.Kind == RepositoryKind.Virtual && Reaches(member, name, rows))
            {
                problems.Add($"Adding '{member}' would make '{name}' contain itself through '{member}'.");
            }
        }

        if (problems.Count == 0 && !string.IsNullOrEmpty(settings.DeployRepository))
        {
            // The deploy repository is judged in the graph with the new members in place of the stored ones.
            var proposed = new Dictionary<string, Repository>(rows, StringComparer.Ordinal)
            {
                [name] = new()
                {
                    Key = rows.TryGetValue(name, out var existing) ? existing.Key : 0,
                    Name = name,
                    Format = format,
                    Kind = RepositoryKind.Virtual,
                    Settings = VirtualSettingsCodec.Serialize(settings),
                },
            };

            if (VirtualRepositories.ResolveDeploy(name, proposed) == null)
            {
                problems.Add($"The deploy repository '{settings.DeployRepository}' must be a local repository (or a virtual one that deploys to a local repository); a remote repository takes no deployments.");
            }
        }

        if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems), nameof(settings));
    }
}
