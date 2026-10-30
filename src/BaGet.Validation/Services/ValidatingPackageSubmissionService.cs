using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using BaGet.Validation.Pipeline;
using BaGet.Validation.Queue;

namespace BaGet.Validation.Services;

/// <summary>
/// With validation on, stages a pushed package and queues it; otherwise indexes it at
/// once, exactly as before. With the <see cref="MaintenanceGate"/> closed, a staged push is refused with
/// <see cref="MaintenanceException"/> before anything reaches storage.
/// </summary>
public class ValidatingPackageSubmissionService : IPackageSubmissionService
{
    private readonly IPackageIndexingService _indexer;
    private readonly IPackageDatabase _packages;
    private readonly IContext _context;
    private readonly IStorageService _storage;
    private readonly IValidationQueue _queue;
    private readonly IEnumerable<IPackageValidator> _validators;
    private readonly SystemTime _time;
    private readonly IOptionsSnapshot<BaGetOptions> _options;
    private readonly IOptionsSnapshot<ValidationOptions> _validation;
    private readonly ILogger<ValidatingPackageSubmissionService> _logger;
    private readonly MaintenanceGate? _gate;
    private readonly IRepositoryContext? _repository;

    public ValidatingPackageSubmissionService(
        IPackageIndexingService indexer,
        IPackageDatabase packages,
        IContext context,
        IStorageService storage,
        IValidationQueue queue,
        IEnumerable<IPackageValidator> validators,
        SystemTime time,
        IOptionsSnapshot<BaGetOptions> options,
        IOptionsSnapshot<ValidationOptions> validation,
        ILogger<ValidatingPackageSubmissionService> logger,
        MaintenanceGate? gate = null,
        IRepositoryContext? repository = null)
    {
        _gate = gate;
        _repository = repository;
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _validators = validators ?? throw new ArgumentNullException(nameof(validators));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _validation = validation ?? throw new ArgumentNullException(nameof(validation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Where a submission is staged: <c>validation/{id}.nupkg</c> for the default repository,
    /// <c>repos/{name}/validation/{id}.nupkg</c> for any other (see <see cref="RepositoryStorage"/>).
    /// </summary>
    internal static string StagedPath(RepositoryInfo repository, string submissionId)
        => RepositoryStorage.Scope(repository, $"validation/{submissionId}.nupkg");

    public async Task<PackageIndexingResult> SubmitAsync(Stream package, CancellationToken cancellationToken)
    {
        if (!_validation.Value.Enabled)
        {
            return await _indexer.IndexAsync(package, cancellationToken);
        }

        // Refused up front, before the package reaches storage: staging it and then failing to record it in the
        // database (which is closed) would leave an orphan file behind.
        _gate?.ThrowIfClosed();

        string id;
        string version;
        try
        {
            using (var reader = new PackageArchiveReader(package, leaveStreamOpen: true))
            {
                var identity = reader.GetIdentity();
                id = identity.Id;
                version = identity.Version.ToNormalizedString();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Uploaded package is invalid");
            return PackageIndexingResult.InvalidPackage;
        }

        if (!_options.Value.AllowPackageOverwrites
            && await _packages.ExistsAsync(id, NuGet.Versioning.NuGetVersion.Parse(version), cancellationToken))
        {
            return PackageIndexingResult.PackageAlreadyExists;
        }

        // The same version cannot be validated twice at once.
        var lowerId = id.ToLowerInvariant();
        #pragma warning disable CS8602, CS8604
        if (await _context.PackageValidations.AnyAsync(
        #pragma warning restore CS8604
                v => v.PackageId.ToLower() == lowerId
        #pragma warning restore CS8602
                     && v.PackageVersion == version
                     && v.State == PackageValidationState.Pending,
                cancellationToken))
        {
            return PackageIndexingResult.PackageAlreadyExists;
        }

        var submissionId = Guid.NewGuid().ToString("D");

        package.Position = 0;
        await _storage.PutAsync(StagedPath(_repository?.Current ?? RepositoryInfo.Default, submissionId), package, "application/octet-stream", cancellationToken);

        var now = _time.UtcNow;
        var steps = _validators.Select(v => v.Name).Append(PackageValidation.IndexType);
        foreach (var step in steps)
        {
            _context.PackageValidations.Add(new PackageValidation
            {
                SubmissionId = submissionId,
                PackageId = id,
                PackageVersion = version,
                Type = step,
                State = PackageValidationState.Pending,
                Created = now,
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _queue.EnqueueAsync(submissionId, cancellationToken);

        _logger.LogInformation(
            "Queued package {PackageId} {PackageVersion} for validation as {SubmissionId}",
            id,
            version,
            submissionId);

        return PackageIndexingResult.Accepted;
    }
}