using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Holds a package between the two requests of a browser upload: the first stages it and
/// shows what it contains, the second confirms it. The upload is stored, not held in the
/// request, so the two requests may land on different instances.
/// </summary>
public interface IPackageStagingService
{
    /// <summary>
    /// Stage an upload for <paramref name="ownerId"/>.
    /// </summary>
    /// <returns>An opaque token identifying the staged upload.</returns>
    Task<string> StageAsync(Stream? package, string? ownerId, CancellationToken cancellationToken = default);

    /// <returns>
    /// The staged package, or null if the token is unknown, incomplete, or was staged by
    /// a different user.
    /// </returns>
    Task<Stream?> OpenAsync(string token, string? ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forget a staged upload, whether it was confirmed or cancelled. The owner is
    /// checked exactly as <see cref="OpenAsync"/> checks it: the token is the only handle
    /// on an upload, so without this anyone who learned one could discard someone else's
    /// pending upload. An unknown token is not an error.
    /// </summary>
    /// <returns>
    /// Whether an upload was removed. False when the token is unknown or belongs to a
    /// different user, which are deliberately indistinguishable.
    /// </returns>
    Task<bool> RemoveAsync(string? token, string? ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove every upload staged before <paramref name="stagedBefore"/>. An upload that
    /// is never confirmed nor cancelled - a closed browser tab - is only collected here,
    /// so without this staging grows without bound.
    /// </summary>
    /// <returns>How many staged uploads were removed.</returns>
    Task<int> SweepAsync(DateTime stagedBefore, CancellationToken cancellationToken = default);
}