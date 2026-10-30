using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Validation.Pipeline;

/// <summary>A validation this process is running: lets a reject cancel it and wait for its end.</summary>
internal sealed class ActiveRun
{
    public CancellationTokenSource Cancel { get; } = new();
    public TaskCompletionSource<bool> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
