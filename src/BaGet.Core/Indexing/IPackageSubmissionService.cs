using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Accepts a package a user pushed. Unlike <see cref="IPackageIndexingService"/>, which
/// always indexes at once and which mirroring uses, a submission may be validated first.
/// </summary>
public interface IPackageSubmissionService
{
    /// <returns>
    /// <see cref="PackageIndexingResult.Accepted"/> if the package is queued for
    /// validation, otherwise the result of indexing it.
    /// </returns>
    Task<PackageIndexingResult> SubmitAsync(Stream package, CancellationToken cancellationToken);
}

/// <summary>
/// Indexes every submission at once: the behaviour without validation.
/// </summary>
public class DirectPackageSubmissionService : IPackageSubmissionService
{
    private readonly IPackageIndexingService _indexer;

    public DirectPackageSubmissionService(IPackageIndexingService indexer)
    {
        _indexer = indexer ?? throw new System.ArgumentNullException(nameof(indexer));
    }

    public Task<PackageIndexingResult> SubmitAsync(Stream? package, CancellationToken cancellationToken)
        => _indexer.IndexAsync(package, cancellationToken);
}