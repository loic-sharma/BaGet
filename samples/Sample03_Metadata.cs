using System;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Samples.Tests
{
    public class Sample03_Metadata
    {
        [Fact]
        public async Task GetSingleVersion()
        {
            // Find all versions of a package (excludes unlisted versions).
            var clientFactory = new NuGetClientFactory("https://api.nuget.org/v3/index.json");
            var packageMetadata = clientFactory.CreatePackageMetadataClient();

            var packageId = "Newtonsoft.Json";
            var packageVersion = new NuGetVersion("12.0.1");
            var cancellationToken = CancellationToken.None;

            var registrationItem = await packageMetadata.GetRegistrationItemOrNullAsync(packageId, packageVersion, cancellationToken);
            if (registrationItem == null)
            {
                Console.WriteLine($"Package '{packageId}' version '{packageVersion}' does not exist");
                return;
            }

            Console.WriteLine($"Listed: {registrationItem.PackageMetadata.Listed}");
            Console.WriteLine($"Tags: {registrationItem.PackageMetadata.Tags}");
            Console.WriteLine($"Description: {registrationItem.PackageMetadata.Description}");
        }

        [Fact]
        public async Task GetAllVersions()
        {
            // Find all versions of a package.
            var clientFactory = new NuGetClientFactory("https://api.nuget.org/v3/index.json");
            var packageMetadata = clientFactory.CreatePackageMetadataClient();

            var packageId = "Newtonsoft.Json";
            var cancellationToken = CancellationToken.None;

            var registrationItems = await packageMetadata.GetRegistrationItemsOrNullAsync(packageId, cancellationToken);
            if (registrationItems == null)
            {
                Console.WriteLine($"Package '{packageId}' does not exist");
                return;
            }

            foreach (var item in registrationItems)
            {
                Console.WriteLine($"Version: {item.PackageMetadata.Version}");
                Console.WriteLine($"Listed: {item.PackageMetadata.Listed}");
                Console.WriteLine($"Tags: {item.PackageMetadata.Tags}");
                Console.WriteLine($"Description: {item.PackageMetadata.Description}");
            }
        }

        [Fact]
        public async Task ListVersions()
        {
            // Find all versions of a package (including unlisted versions).
            var clientFactory = new NuGetClientFactory("https://api.nuget.org/v3/index.json");
            var packageContent = clientFactory.CreatePackageContentClient();

            var packageId = "Newtonsoft.Json";
            var cancellationToken = CancellationToken.None;

            var packageVersions = await packageContent.GetPackageVersionsOrNullAsync(packageId, cancellationToken);
            if (packageVersions == null)
            {
                Console.WriteLine($"Package '{packageId}' does not exist");
                return;
            }

            Console.WriteLine($"Found {packageVersions.Versions.Count} versions");
        }
    }
}
