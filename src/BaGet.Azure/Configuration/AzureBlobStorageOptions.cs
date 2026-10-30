using System;
using System.Collections.Generic;
using BaGet.Core;
using System.ComponentModel.DataAnnotations;

namespace BaGet.Azure;

/// <summary>
/// BaGet's configurations to use Azure Blob Storage to store packages.
/// See: https://pieroviano.github.io/BaGet/quickstart/azure/#azure-blob-storage
/// </summary>
[RestartRequired]
public class AzureBlobStorageOptions : IValidatableObject
{
    /// <summary>
    /// The Azure Blob Storage connection string.
    /// If provided, ignores <see cref="AccountName"/> and <see cref="AccessKey"/>.
    /// </summary>
    [SensitiveSetting]
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The Azure Blob Storage account name. Ignored if <see cref="ConnectionString"/> is provided.
    /// </summary>
    public string? AccountName { get; set; }

    /// <summary>
    /// The Azure Blob Storage access key. Ignored if <see cref="ConnectionString"/> is provided.
    /// </summary>        
    [SensitiveSetting]
    public string? AccessKey { get; set; }

    /// <summary>
    /// The Azure Blob Storage container name.
    /// </summary>
    public string? Container { get; set; }

    /// <summary>
    /// How long the shared access signatures handed out by
    /// <see cref="BlobStorageService.GetDownloadUriAsync"/> stay valid.
    /// Must be greater than zero.
    /// </summary>
    public TimeSpan DownloadUriExpiry { get; set; } = DefaultDownloadUriExpiry;

    /// <summary>
    /// The <see cref="DownloadUriExpiry"/> used when the configuration does not set one.
    /// </summary>
    public static readonly TimeSpan DefaultDownloadUriExpiry = TimeSpan.FromMinutes(10);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        const string helpUrl = "https://pieroviano.github.io/BaGet/quickstart/azure/#azure-blob-storage";

        if (DownloadUriExpiry <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"The {nameof(DownloadUriExpiry)} configuration must be greater than zero. See {helpUrl}",
                new[] { nameof(DownloadUriExpiry) });
        }

        if (string.IsNullOrEmpty(ConnectionString))
        {
            if (string.IsNullOrEmpty(AccountName))
            {
                yield return new ValidationResult(
                    $"The {nameof(AccountName)} configuration is required. See {helpUrl}",
                    new[] { nameof(AccountName) });
            }

            if (string.IsNullOrEmpty(AccessKey))
            {
                yield return new ValidationResult(
                    $"The {nameof(AccessKey)} configuration is required. See {helpUrl}",
                    new[] { nameof(AccessKey) });
            }
        }

        if (string.IsNullOrEmpty(Container))
        {
            yield return new ValidationResult(
                $"The {nameof(Container)} configuration is required. See {helpUrl}",
                new[] { nameof(Container) });
        }
    }
}
