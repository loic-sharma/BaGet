using BaGet.Azure.Configuration;
using BaGet.Core.Configuration;

namespace BaGet.Azure.Extensions
{
    using CoreOptionsExtensions = Core.Extensions.OptionsExtensions;

    public static class OptionsExtensions
    {
        public static void EnsureValid(this BlobStorageOptions options)
        {
            if (options == null) CoreOptionsExtensions.ThrowMissingConfiguration(nameof(BaGetOptions.Storage));

            if (string.IsNullOrEmpty(options.AccountName))
            {
                CoreOptionsExtensions.ThrowMissingConfiguration(
                    nameof(BaGetOptions.Storage),
                    nameof(BlobStorageOptions.AccountName));
            }

            if (string.IsNullOrEmpty(options.AccessKey))
            {
                CoreOptionsExtensions.ThrowMissingConfiguration(
                    nameof(BaGetOptions.Storage),
                    nameof(BlobStorageOptions.AccessKey));
            }

            if (string.IsNullOrEmpty(options.Container))
            {
                CoreOptionsExtensions.ThrowMissingConfiguration(
                    nameof(BaGetOptions.Storage),
                    nameof(BlobStorageOptions.Container));
            }
        }

        public static void EnsureValid(this AzureSearchOptions options)
        {
            if (options == null) CoreOptionsExtensions.ThrowMissingConfiguration(nameof(BaGetOptions.Search));

            if (string.IsNullOrEmpty(options.AccountName))
            {
                CoreOptionsExtensions.ThrowMissingConfiguration(
                    nameof(BaGetOptions.Search),
                    nameof(AzureSearchOptions.AccountName));
            }

            if (string.IsNullOrEmpty(options.AccountName))
            {
                CoreOptionsExtensions.ThrowMissingConfiguration(
                    nameof(BaGetOptions.Search),
                    nameof(AzureSearchOptions.ApiKey));
            }
        }
    }
}
