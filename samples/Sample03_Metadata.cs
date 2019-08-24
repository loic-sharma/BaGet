using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Samples.Tests
{
    public class Sample03_Metadata
    {
        [Fact]
        public async Task GetAllPackageMetadata()
        {
            // Find the metadata for all versions of a package.
            var client = new NuGetClient("https://api.nuget.org/v3/index.json");

            var items = await client.GetPackageMetadataAsync("Newtonsoft.Json");
            if (!items.Any())
            {
                Console.WriteLine($"Package 'Newtonsoft.Json' does not exist");
                return;
            }

            // There is an item for each version of the package.
            foreach (var item in items)
            {
                Console.WriteLine($"Version: {item.PackageMetadata.Version}");
                Console.WriteLine($"Listed: {item.PackageMetadata.Listed}");
                Console.WriteLine($"Tags: {item.PackageMetadata.Tags}");
                Console.WriteLine($"Description: {item.PackageMetadata.Description}");
            }
        }

        [Fact]
        public async Task GetPackageMetadata()
        {
            // Find the metadata for a single version of a package.
            var client = new NuGetClient("https://api.nuget.org/v3/index.json");

            var packageId = "Newtonsoft.Json";
            var packageVersion = new NuGetVersion("12.0.1");

            var registrationItem = await client.GetPackageMetadataAsync(packageId, packageVersion);

            Console.WriteLine($"Listed: {registrationItem.PackageMetadata.Listed}");
            Console.WriteLine($"Tags: {registrationItem.PackageMetadata.Tags}");
            Console.WriteLine($"Description: {registrationItem.PackageMetadata.Description}");
        }

        [Fact]
        public async Task ListVersions()
        {
            // Find all versions of a package (including unlisted versions).
            var client = new NuGetClient("https://api.nuget.org/v3/index.json");

            var packageVersions = await client.ListPackageVersions("Newtonsoft.Json", includeUnlisted: true);
            if (!packageVersions.Any())
            {
                Console.WriteLine($"Package 'Newtonsoft.Json' does not exist");
                return;
            }

            Console.WriteLine($"Found {packageVersions.Count} versions");
        }
    }
}
