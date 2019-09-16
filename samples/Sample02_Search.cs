using System;
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
            SearchResponse response = await client.SearchAsync("json");

            Console.WriteLine($"Found {response.TotalHits} results");

            var index = 1;
            foreach (SearchResult searchResult in response.Data)
            {
                Console.WriteLine($"Result #{index}");
                Console.WriteLine($"Package id: {searchResult.PackageId}");
                Console.WriteLine($"Package version: {searchResult.Version}");
                Console.WriteLine($"Package downloads: {searchResult.TotalDownloads}");
                Console.WriteLine($"Package versions: {searchResult.Versions.Count}");
                Console.WriteLine();

                index++;
            }
        }

        [Fact]
        public async Task Autocomplete()
        {
            // Search for packages whose names' start with "Newt".
            NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");
            AutocompleteResponse response = await client.AutocompleteAsync("Newt");

            Console.WriteLine($"Found {response.TotalHits} results");

            var index = 1;
            foreach (string packageId in response.Data)
            {
                Console.WriteLine($"Found package ID #{index}: '{packageId}'");
                index++;
            }
        }
    }
}
