using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Protocol.Samples.Tests
{
    public class Sample02_Search
    {
        [Fact]
        public async Task Search()
        {
            // Searches for "json" packages, including prerelease packages.
            var clientFactory = new NuGetClientFactory("https://api.nuget.org/v3/index.json");
            var searchClient = clientFactory.CreateSearchClient();

            var cancellationToken = CancellationToken.None;
            var searchRequest = new SearchRequest
            {
                Query = "json",
                IncludePrerelease = true,

                // You can exclude packages that use semver2 versioning
                // IncludeSemVer2 = false,

                // You can skip results
                // Skip = 10,

                // You can limit the number of returned results
                // Take = 1,
            };

            var response = await searchClient.SearchAsync(searchRequest, cancellationToken);

            Console.WriteLine($"Found {response.TotalHits} results for query {searchRequest.Query}");

            var index = 1;
            foreach (var searchResult in response.Data)
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
            // Searches for packages whose name start with "Newt", including prerelease packages.
            var clientFactory = new NuGetClientFactory("https://api.nuget.org/v3/index.json");
            var searchClient = clientFactory.CreateSearchClient();

            var cancellationToken = CancellationToken.None;
            var autocompleteRequest = new AutocompleteRequest
            {
                Query = "Newt",
                IncludePrerelease = true

                // You can exclude packages that use semver2 versioning
                // IncludeSemVer2 = false,

                // You can skip results
                // Skip = 10,

                // You can limit the number of returned results
                // Take = 1,
            };

            // TODO: Split autocomplete and search clients.
            var response = await searchClient.AutocompleteAsync(autocompleteRequest, cancellationToken);

            Console.WriteLine($"Found {response.TotalHits} results for query {autocompleteRequest.Query}");

            var index = 1;
            foreach (var searchResult in response.Data)
            {
                Console.WriteLine($"Result #{index}: '{searchResult}'");
                index++;
            }
        }
    }
}
