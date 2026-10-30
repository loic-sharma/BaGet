using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// An optional capability of an <see cref="IStorageService"/>: list what it holds under a
/// path prefix. Content that is only ever read back through the path that stored it does
/// not need this; collecting content whose paths nobody remembers - abandoned staged
/// uploads, for one - does.
/// </summary>
/// <remarks>
/// A store that does not implement this cannot be swept, so a caller must degrade
/// rather than cast and assume.
/// </remarks>
public interface IListableStorageService
{
    /// <param name="pathPrefix">
    /// The prefix to list, as a path of one or more segments ("staging").
    /// </param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>
    /// The path of every item under the prefix, relative to the store's root and
    /// separated with "/", so each can be passed straight back to
    /// <see cref="IStorageService"/>. Empty if the prefix holds nothing.
    /// </returns>
    Task<IReadOnlyList<string>> ListAsync(string pathPrefix, CancellationToken cancellationToken = default);
}