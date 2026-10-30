using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>
/// Answers what a <see cref="RepositoryKind.Virtual"/> repository is made of, from the stored settings and without any
/// access check (see <see cref="VirtualRepositoryReader"/> for what the caller may read).
/// </summary>
public interface IVirtualRepositories
{
    /// <summary>
    /// The repositories that answer for <paramref name="virtualRepository"/>, in resolution order: its members, with a
    /// member that is itself virtual replaced in place by its own members. Each repository appears once (its first
    /// place counts), a cycle is cut where it closes, and members that no longer exist or are of another format than the
    /// virtual repository are skipped. The result holds local and remote repositories only.
    /// </summary>
    Task<IReadOnlyList<RepositoryInfo>> EffectiveMembersAsync(RepositoryInfo virtualRepository, CancellationToken cancellationToken);

    /// <summary>
    /// The local repository that receives what is pushed to <paramref name="virtualRepository"/>: its deploy repository,
    /// or, when that is virtual too, the one it deploys to. Null when none is set or the chain does not end in a local
    /// repository.
    /// </summary>
    Task<RepositoryInfo?> DeployTargetAsync(RepositoryInfo virtualRepository, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IVirtualRepositories"/> over the repositories table. Scoped: the table is read once per instance (it is
/// a handful of rows), so a long-lived instance does not see a later change; take a new scope for that.
/// </summary>
public sealed class VirtualRepositories : IVirtualRepositories
{
    private const int MaxDepth = 16;

    private readonly IContext _context;
    private Dictionary<string, Repository>? _byName;

    public VirtualRepositories(IContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RepositoryInfo>> EffectiveMembersAsync(RepositoryInfo virtualRepository, CancellationToken cancellationToken)
    {
        if (virtualRepository == null) throw new ArgumentNullException(nameof(virtualRepository));

        var rows = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<RepositoryInfo>();
        var keys = new HashSet<int>();
        Flatten(virtualRepository.Name, rows, new HashSet<string>(StringComparer.Ordinal), result, keys, depth: 0);

        return result;
    }

    /// <inheritdoc/>
    public async Task<RepositoryInfo?> DeployTargetAsync(RepositoryInfo virtualRepository, CancellationToken cancellationToken)
    {
        if (virtualRepository == null) throw new ArgumentNullException(nameof(virtualRepository));

        var rows = await LoadAsync(cancellationToken).ConfigureAwait(false);

        return ResolveDeploy(virtualRepository.Name, rows);
    }

    /// <summary>The deploy target of the virtual repository <paramref name="name"/> in <paramref name="rows"/>, or null.</summary>
    internal static RepositoryInfo? ResolveDeploy(string name, IReadOnlyDictionary<string, Repository> rows)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = name;
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (!visited.Add(current) || !rows.TryGetValue(current, out var row) || row.Kind != RepositoryKind.Virtual) return null;

            var next = VirtualSettingsCodec.Deserialize(row.Settings).DeployRepository;
            if (string.IsNullOrEmpty(next) || !rows.TryGetValue(next!, out var target)) return null;
            if (target.Format != row.Format) return null;
            if (target.Kind == RepositoryKind.Local) return target.ToInfo();
            if (target.Kind != RepositoryKind.Virtual) return null;

            current = next!;
        }

        return null;
    }

    // Depth-first, in member order. 'path' holds the virtual repositories being expanded, which is what detects a cycle.
    internal static void Flatten(
        string name,
        IReadOnlyDictionary<string, Repository> rows,
        HashSet<string> path,
        List<RepositoryInfo> result,
        HashSet<int> keys,
        int depth)
    {
        if (depth > MaxDepth || !path.Add(name)) return;
        if (!rows.TryGetValue(name, out var row)) { path.Remove(name); return; }

        foreach (var memberName in VirtualSettingsCodec.Deserialize(row.Settings).Members)
        {
            // A virtual repository is of one format: its members are too.
            if (!rows.TryGetValue(memberName, out var member) || member.Format != row.Format) continue;

            if (member.Kind == RepositoryKind.Virtual)
            {
                Flatten(memberName, rows, path, result, keys, depth + 1);
            }
            else if (keys.Add(member.Key))
            {
                result.Add(member.ToInfo());
            }
        }

        path.Remove(name);
    }

    private async Task<Dictionary<string, Repository>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_byName != null) return _byName;

        var rows = _context.Repositories == null
            ? new List<Repository>()
            : await _context.Repositories.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return _byName = rows
            .Where(r => r.Name != null)
            .ToDictionary(r => r.Name!, StringComparer.Ordinal);
    }
}
