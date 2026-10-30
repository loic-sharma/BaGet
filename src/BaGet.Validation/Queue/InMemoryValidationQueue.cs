using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BaGet.Validation.Queue;

public class InMemoryValidationQueue : IValidationQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(string submissionId, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(submissionId, cancellationToken);

    public IAsyncEnumerable<string> DequeueAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}