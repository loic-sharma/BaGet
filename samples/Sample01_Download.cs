using System;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Samples.Tests
{
    public class Sample01_Download
    {
        [Fact]
        public async Task DownloadPackage()
        {
            // Downloads a package file (.nupkg)
            var clientFactory = new NuGetClientFactory("https://api.nuget.org/v3/index.json");
            var packageContent = clientFactory.CreatePackageContentClient();

            var packageId = "Newtonsoft.Json";
            var packageVersion = new NuGetVersion("12.0.1");
            var cancellationToken = CancellationToken.None;

            using (var packageStream = await packageContent.GetPackageContentStreamOrNullAsync(packageId, packageVersion, cancellationToken))
            {
                if (packageStream == null)
                {
                    Console.WriteLine($"Package {packageId} {packageVersion} does not exist");
                    return;
                }

                Console.WriteLine($"Downloaded package {packageId} {packageVersion}");
            }
        }

        [Fact]
        public async Task DownloadPackageManifest()
        {
            // Downloads a package manifest (.nuspec)
            var clientFactory = new NuGetClientFactory("https://api.nuget.org/v3/index.json");
            var packageContent = clientFactory.CreatePackageContentClient();

            var packageId = "Newtonsoft.Json";
            var packageVersion = new NuGetVersion("12.0.1");
            var cancellationToken = CancellationToken.None;

            using (var manifestStream = await packageContent.GetPackageManifestStreamOrNullAsync(packageId, packageVersion, cancellationToken))
            {
                if (manifestStream == null)
                {
                    Console.WriteLine($"Package {packageId} {packageVersion} does not exist");
                    return;
                }

                Console.WriteLine($"Downloaded package {packageId} {packageVersion}'s nuspec");
            }
        }
    }
}
