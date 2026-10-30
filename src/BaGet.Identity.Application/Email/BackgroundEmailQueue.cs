using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BaGet.Identity.Application.Email;

/// <summary>
/// Sends email off the request path, so that how long a page takes does not depend on
/// whether anything was sent: "forgot password" must not reveal which accounts exist.
/// </summary>
public class BackgroundEmailQueue
{
    private readonly Channel<Func<IServiceProvider, Task>> _channel = Channel.CreateBounded<Func<IServiceProvider, Task>>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>
    /// Queue work, run with a scope's services. False when the queue is full.
    /// </summary>
    public bool TryEnqueue(Func<IServiceProvider, Task> work) => _channel.Writer.TryWrite(work);

    internal System.Collections.Generic.IAsyncEnumerable<Func<IServiceProvider, Task>> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
