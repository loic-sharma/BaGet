using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;
using Microsoft.Extensions.Options;

namespace BaGet
{
    public class ValidateBaGetOptions : IValidateOptions<BaGetOptions>
    {
        private static readonly HashSet<string> ValidDatabaseTypes
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AzureTable",
                "MySql",
                "PostgreSql",
                "Sqlite",
                "SqlServer",
            };

        private static readonly HashSet<string> ValidStorageTypes
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AzureBlobStorage",
                "Filesystem",
            };

        private static readonly HashSet<string> ValidSearchTypes
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Database",
                "AzureSearch",
            };

        public ValidateOptionsResult Validate(string name, BaGetOptions options)
        {
            var failures = new List<string>();

            if (options.Database == null) failures.Add($"The '{nameof(BaGetOptions.Database)}' config is required");
            if (options.Mirror == null) failures.Add($"The '{nameof(BaGetOptions.Mirror)}' config is required");
            if (options.Search == null) failures.Add($"The '{nameof(BaGetOptions.Search)}' config is required");
            if (options.Storage == null) failures.Add($"The '{nameof(BaGetOptions.Storage)}' config is required");

            if (!ValidDatabaseTypes.Contains(options.Database?.Type))
            {
                failures.Add(
                    $"The '{nameof(BaGetOptions.Database)}:{nameof(DatabaseOptions.Type)}' config is invalid. " +
                    $"Allowed values: {string.Join(", ", ValidDatabaseTypes)}");
            }

            if (!ValidStorageTypes.Contains(options.Storage?.Type))
            {
                failures.Add(
                    $"The '{nameof(BaGetOptions.Storage)}:{nameof(StorageOptions.Type)}' config is invalid. " +
                    $"Allowed values: {string.Join(", ", ValidStorageTypes)}");
            }

            if (!ValidSearchTypes.Contains(options.Search?.Type))
            {
                failures.Add(
                    $"The '{nameof(BaGetOptions.Search)}:{nameof(SearchOptions.Type)}' config is invalid. " +
                    $"Allowed values: {string.Join(", ", ValidSearchTypes)}");
            }

            if (failures.Any()) return ValidateOptionsResult.Fail(failures);

            return ValidateOptionsResult.Success;
        }
    }
}
