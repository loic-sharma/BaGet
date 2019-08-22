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
        public async Task GetSingleVersion()
        {
            // Find all versions of a package (excludes unlisted versions).
            var client = new NuGetClient("https://api.nuget.org/v3/index.json");

            var packageId = "Newtonsoft.Json";
            var packageVersion = new NuGetVersion("12.0.1");

            try
            {
                var registrationItem = await client.GetPackageMetadataAsync(packageId, packageVersion);

                Console.WriteLine($"Listed: {registrationItem.PackageMetadata.Listed}");
                Console.WriteLine($"Tags: {registrationItem.PackageMetadata.Tags}");
                Console.WriteLine($"Description: {registrationItem.PackageMetadata.Description}");
            }
            catch (PackageNotFoundException)
            {
                Console.WriteLine($"Package '{packageId}' version '{packageVersion}' does not exist");
            }
        }

        [Fact]
        public async Task GetAllVersions()
        {
            // Find all versions of a package.
            var client = new NuGetClient("https://api.nuget.org/v3/index.json");

            var packageId = "Newtonsoft.Json";

            var items = await client.GetPackageMetadataAsync(packageId);
            if (!items.Any())
            {
                Console.WriteLine($"Package '{packageId}' does not exist");
                return;
            }

            foreach (var item in items)
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
            var client = new NuGetClient("https://api.nuget.org/v3/index.json");

            var packageId = "Newtonsoft.Json";

            var packageVersions = await client.ListPackageVersions(packageId, includeUnlisted: true);
            if (!packageVersions.Any())
            {
                Console.WriteLine($"Package '{packageId}' does not exist");
                return;
            }

            Console.WriteLine($"Found {packageVersions.Count} versions");
        }
    }
}
