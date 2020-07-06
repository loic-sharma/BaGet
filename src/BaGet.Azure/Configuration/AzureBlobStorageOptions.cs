using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BaGet.Azure
{
    /// <summary>
    /// BaGet's configurations to use Azure Blob Storage to store packages.
    /// See: https://loic-sharma.github.io/BaGet/quickstart/azure/#azure-blob-storage
    /// </summary>
    public class AzureBlobStorageOptions : IValidatableObject
    {
        /// <summary>
        /// The Azure Blob Storage connection string.
        /// If provided, ignores <see cref="AccountName"/> and <see cref="AccessKey"/>.
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// The Azure Blob Storage account name. Ignored if <see cref="ConnectionString"/> is provided.
        /// </summary>
        public string AccountName { get; set; }

        /// <summary>
        /// The Azure Blob Storage access key. Ignored if <see cref="ConnectionString"/> is provided.
        /// </summary>        
        public string AccessKey { get; set; }

        /// <summary>
        /// The Azure Blob Storage container name.
        /// </summary>
        public string Container { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            const string helpUrl = "https://loic-sharma.github.io/BaGet/quickstart/azure/#azure-blob-storage";

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
}
