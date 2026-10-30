using System;

namespace BaGet.Core;

public enum PackageValidationState
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}

/// <summary>
/// One step of validating a pushed package: a validator's verdict, or the final
/// indexing step. A submission is every row sharing a <see cref="SubmissionId"/>.
/// </summary>
/// <remarks>
/// The package does not exist while it is validated: it is staged in storage and only
/// indexed once every validator succeeds. So nothing that serves packages needs to
/// know about validation, and a package being validated cannot be downloaded.
/// </remarks>
public class PackageValidation : IRepositoryScoped
{
    /// <summary>
    /// The <see cref="Type"/> of the step that indexes the package once it is valid.
    /// </summary>
    public const string IndexType = "Index";

    public int Key { get; set; }

    /// <summary>
    /// The owning repository; see <see cref="IRepositoryScoped"/>.
    /// </summary>
    public int RepositoryKey { get; set; }

    public string? SubmissionId { get; set; }

    public string? PackageId { get; set; }

    /// <summary>
    /// The normalized version.
    /// </summary>
    public string? PackageVersion { get; set; }

    /// <summary>
    /// The indexed package, once the submission succeeds. Nullable so that the
    /// relationship is additive; see <see cref="PackageDeprecation"/>.
    /// </summary>
    public int? PackageKey { get; set; }

    public Package? Package { get; set; }

    /// <summary>
    /// The validator's name, or <see cref="IndexType"/>.
    /// </summary>
    public string? Type { get; set; }

    public PackageValidationState State { get; set; }

    /// <summary>
    /// Why the step failed, if it did.
    /// </summary>
    public string? Message { get; set; }

    public DateTime Created { get; set; }

    public DateTime? Completed { get; set; }
}