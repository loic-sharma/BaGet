using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// Calls every registered <see cref="IPackageChangeHandler"/>. The change has already
/// been committed when this runs, so a handler's failure is logged and never fails the
/// operation that made the change.
/// </summary>
public class PackageChangeNotifier
{
    /// <summary>
    /// A notifier with no handlers, for callers constructed without one.
    /// </summary>
    public static readonly PackageChangeNotifier None = new(Array.Empty<IPackageChangeHandler>(), logger: null);

    private readonly IReadOnlyList<IPackageChangeHandler> _handlers;
    private readonly ILogger<PackageChangeNotifier>? _logger;

    public PackageChangeNotifier(
        IEnumerable<IPackageChangeHandler>? handlers,
        ILogger<PackageChangeNotifier>? logger)
    {
        _handlers = handlers?.ToList() ?? throw new ArgumentNullException(nameof(handlers));
        _logger = logger;
    }

    public Task PackageChangedAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
        => NotifyAsync(id, version, (h, ct) => h.OnPackageChangedAsync(id, version, ct), cancellationToken);

    public Task PackageDeletedAsync(string? id, NuGetVersion version, CancellationToken cancellationToken)
        => NotifyAsync(id, version, (h, ct) => h.OnPackageDeletedAsync(id, version, ct), cancellationToken);

    private async Task NotifyAsync(
        string? id,
        NuGetVersion version,
        Func<IPackageChangeHandler, CancellationToken, Task> notify,
        CancellationToken cancellationToken)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                await notify(handler, cancellationToken);
            }
            catch (Exception e) when (!(e is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                _logger?.LogError(
                    e,
                    "{Handler} failed for package {PackageId} {PackageVersion}",
                    handler.GetType().Name,
                    id,
                    version);
            }
        }
    }
}