using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Optional capability of an <see cref="IJobLease"/>: tells "another holder has it" from "the lease backend cannot be reached",
/// both of which make <see cref="IJobLease.TryAcquireAsync"/> answer null.
/// </summary>
public interface IJobLeaseAvailability
{
    /// <summary>True when the backend answers, so a refused lease means another holder.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}
