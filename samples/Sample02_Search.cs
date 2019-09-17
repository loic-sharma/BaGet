using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using Xunit;

namespace BaGet.Protocol.Samples.Tests
{
    public class Sample02_Search
    {
        [Fact]
        public async Task Search()
        {
            // Search for packages that are relevant to "json".
            NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");
            IReadOnlyList<SearchResult> results = await client.SearchAsync("json");

            var index = 1;
            foreach (SearchResult result in results)
            {
                Console.WriteLine($"Result #{index}");
                Console.WriteLine($"Package id: {result.PackageId}");
                Console.WriteLine($"Package version: {result.Version}");
                Console.WriteLine($"Package downloads: {result.TotalDownloads}");
                Console.WriteLine($"Package versions: {result.Versions.Count}");
                Console.WriteLine();

                index++;
            }
        }

        [Fact]
        public async Task Autocomplete()
        {
            // Search for packages whose names' start with "Newt".
            NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");
            IReadOnlyList<string> packageIds = await client.AutocompleteAsync("Newt");

            foreach (string packageId in packageIds)
            {
                Console.WriteLine($"Found package ID '{packageId}'");
            }
        }
    }
}
