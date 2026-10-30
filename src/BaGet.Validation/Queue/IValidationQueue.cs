using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Validation.Queue;

/// <summary>
/// Hands submissions to the validation worker. The submissions themselves are in the
/// database, so a queue only has to say which to run next; the default one is in
/// memory, and on startup the worker re-queues whatever is still pending.
/// </summary>
public interface IValidationQueue
{
    ValueTask EnqueueAsync(string submissionId, CancellationToken cancellationToken);

    IAsyncEnumerable<string> DequeueAllAsync(CancellationToken cancellationToken);
}
