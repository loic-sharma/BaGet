using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Core;

/// <summary>
/// What a repository holds, in the form replication compares: the answer of <c>GET /api/v1/replication/inventory/{repository}</c> (what
/// a peer asks) and the count the replication page shows for the local side. A virtual repository stores nothing and has no items;
/// a remote one lists what it cached.
/// </summary>
public interface IReplicationInventory
{
    /// <summary>The inventory of <paramref name="repository"/>, or null when there is no such repository.</summary>
    /// <param name="repository">The repository name.</param>
    /// <param name="summary">Only the count and the kind (<see cref="ReplicationInventory.Items"/> is empty).</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    Task<ReplicationInventory?> GetAsync(string repository, bool summary, CancellationToken cancellationToken);
}

/// <summary>The default <see cref="IReplicationInventory"/>: each repository is listed in a scope of its own.</summary>
public sealed class ReplicationInventoryService : IReplicationInventory
{
    private readonly IRepositoryService _repositories;
    private readonly IServiceScopeFactory _scopes;

    public ReplicationInventoryService(IRepositoryService repositories, IServiceScopeFactory scopes)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
    }

    /// <inheritdoc/>
    public async Task<ReplicationInventory?> GetAsync(string repository, bool summary, CancellationToken cancellationToken)
    {
        var info = await _repositories.FindAsync(repository, cancellationToken).ConfigureAwait(false);
        if (info == null) return null;
        if (info.Kind == RepositoryKind.Virtual) return new ReplicationInventory(info.Name, info.Format, info.Kind, 0, Array.Empty<ReplicaItem>());

        using var scope = _scopes.CreateRepositoryScope(info);
        var replicator = scope.ServiceProvider.GetFormatService<IFormatReplicator>(info.Format);
        var items = replicator == null ? Array.Empty<ReplicaItem>() : (IReadOnlyList<ReplicaItem>)await replicator.ListAsync(cancellationToken).ConfigureAwait(false);

        return new ReplicationInventory(info.Name, info.Format, info.Kind, items.Count, summary ? Array.Empty<ReplicaItem>() : items);
    }
}
