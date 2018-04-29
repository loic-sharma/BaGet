using BaGet.Core.Services;
using Microsoft.Extensions.Logging;

namespace BaGet.Services.Mirror
{
    /// <summary>
    /// A wrapper around <see cref="IndexingService"/> to prevent infinite recursion
    /// during dependency injection.
    /// </summary>
    /// <remarks>
    /// Both <see cref="IndexingService"/> and <see cref="MirrorPackageService{TPackageService}"/>
    /// depend on an <see cref="IPackageService"/>. To prevent infinite recursion, this type
    /// forces the indexing service to use the <see cref="TPackageService"/>.
    /// </remarks>
    /// <typeparam name="TPackageService">
    /// The local package service used to store package metadata that will be wrapped
    /// by <see cref="MirrorIndexingService{TPackageService}"/>.
    /// </typeparam>
    public class MirrorIndexingService<TPackageService> : IndexingService
        where TPackageService : IPackageService
    {
        public MirrorIndexingService(
            TPackageService packages,
            IPackageStorageService storage,
            ILogger<IndexingService> logger) : base(packages, storage, logger)
        {
        }
    }
}
