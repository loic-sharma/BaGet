using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>
/// Upgrade aid for the access model. Before it existed <see cref="Repository.AnonymousRead"/> could be saved as false
/// and did nothing; now it makes the repository private. The saved setting is kept (the admin chose it), but a
/// repository that is private and has no Read grant for anyone besides administrators is reported at every startup, so
/// the change is visible. The warning stops once the switch is on or a Read grant exists.
/// </summary>
public static class AccessUpgradeCheck
{
    /// <summary>
    /// The non-default repositories with <c>AnonymousRead</c> off in which no grant or project role gives anyone
    /// Read (administrators and the global API key always can, and are not counted).
    /// </summary>
    public static async Task<IReadOnlyList<string>> FindPrivateWithoutReadersAsync(
        IRepositoryService repositories,
        IAccessEvaluator evaluator,
        CancellationToken cancellationToken)
    {
        if (!repositories.SupportsMultipleRepositories) return Array.Empty<string>();

        var result = new List<string>();
        foreach (var repository in await repositories.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (repository.IsDefault) continue;

            var summary = await evaluator.DescribeAsync(repository, cancellationToken).ConfigureAwait(false);
            if (summary.ModelAvailable && !summary.AnonymousRead && !summary.Holders.Any(h => (h.Effective & AccessAction.Read) != 0))
            {
                result.Add(repository.Name);
            }
        }

        return result;
    }

    /// <summary>Logs one Warning naming those repositories; never throws (a startup check must not stop the host).</summary>
    public static async Task WarnAsync(
        IRepositoryService repositories,
        IAccessEvaluator evaluator,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var names = await FindPrivateWithoutReadersAsync(repositories, evaluator, cancellationToken).ConfigureAwait(false);
            if (names.Count > 0)
            {
                logger.LogWarning(
                    "Repositories {Repositories} have anonymous read off and no Read grant for anyone but administrators, so only administrators can read them. "
                    + "Before access control that switch did nothing. Switch anonymous read on, or grant Read, at /admin/repositories",
                    string.Join(", ", names));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "The access upgrade check failed");
        }
    }
}
