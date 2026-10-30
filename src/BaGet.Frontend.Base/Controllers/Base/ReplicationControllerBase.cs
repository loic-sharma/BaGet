using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.UseCases.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Frontend.Controllers.Base;

/// <summary>
/// Base of the replication screens. The access screens' rules
/// (administrators only, 404 for anyone else, redirect where there is no relational database, 503 on a POST while the server is
/// read-only), with the replication services and the repositories a configuration can name. A <see cref="ReplicationException"/>
/// is an <see cref="AccessModelException"/>, so <c>ActAsync</c> turns it into the screen's <c>Error</c>.
/// </summary>
public abstract class ReplicationControllerBase : AccessControllerBase
{
    /// <summary>Configurations and their log.</summary>
    protected IReplicationAdministration Replication => HttpContext.RequestServices.GetRequiredService<IReplicationAdministration>();

    /// <summary>Runs replications in the background.</summary>
    protected ReplicationJobs Jobs => HttpContext.RequestServices.GetRequiredService<ReplicationJobs>();

    /// <summary>The local repositories a replication can name (remote and virtual ones are not replicated), by name.</summary>
    public IReadOnlyList<RepositoryInfo> Repositories { get; private set; } = Array.Empty<RepositoryInfo>();

    /// <summary>Loads <see cref="Repositories"/>.</summary>
    protected async Task LoadChoicesAsync(CancellationToken cancellationToken)
    {
        Repositories = await PolicyScreenSupport.ReplicationRepositoriesAsync(HttpContext.RequestServices.GetRequiredService<IRepositoryService>(), cancellationToken);
    }

    /// <summary>The schedule and triggers of a configuration in a few words.</summary>
    public static string Describe(ReplicationConfig c) => PolicyScreenSupport.DescribeReplication(c);
}
