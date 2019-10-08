using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace Demo1
{
    class Program
    {
        static async Task Main(string[] args)
        {
        }

        private static async Task Demo1()
        {
            var logger = NullLogger.Instance;
            var sourceRepository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json", FeedType.HttpV3);
            var searchResource = await sourceRepository.GetResourceAsync<PackageSearchResourceV3>();

            var cancellationToken = CancellationToken.None;
            var searchFilter = new SearchFilter(includePrerelease: true);

            var results = await searchResource.SearchAsync(
                "mysql database",
                searchFilter,
                skip: 0,
                take: 20,
                logger,
                cancellationToken);

            foreach (var result in results)
            {
                Console.WriteLine($"Found package {result.Identity.Id}");
            }
        }


        private static async Task Demo3()
        {
            var httpClient = new HttpClient();
            var clientFactory = new NuGetClientFactory(httpClient, "https://api.nuget.org/v3/index.json");
            var searchClient = clientFactory.CreateSearchClient();

            var results = await searchClient.SearchAsync("mysql database");

            foreach (var result in results.Data)
            {
                Console.WriteLine($"Found package {result.PackageId}");
            }
        }

        private static async Task Demo2()
        {
            var client = new NuGetClient("https://api.nuget.org/v3/index.json");
            var results = await client.SearchAsync("mysql database");

            foreach (var result in results)
            {
                Console.WriteLine($"Found package {result.PackageId}");
            }
        }
    }
}
