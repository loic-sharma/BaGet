using System;
using System.IO;
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
            NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");

            string packageId = "Newtonsoft.Json";
            NuGetVersion packageVersion = new NuGetVersion("12.0.1");

            using (Stream packageStream = await client.DownloadPackageAsync(packageId, packageVersion))
            {
                Console.WriteLine($"Downloaded package {packageId} {packageVersion}");
            }
        }

        [Fact]
        public async Task DownloadPackageManifest()
        {
            // Downloads a package manifest (.nuspec)
            NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");

            string packageId = "Newtonsoft.Json";
            NuGetVersion packageVersion = new NuGetVersion("12.0.1");

            using (Stream manifestStream = await client.DownloadPackageManifestAsync(packageId, packageVersion))
            {
                Console.WriteLine($"Downloaded package {packageId} {packageVersion}'s nuspec");
            }
        }
    }
}
