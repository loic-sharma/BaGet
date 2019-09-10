using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Protocol.Catalog;
using NuGet.Packaging.Core;
using Xunit;

namespace BaGet.Protocol.Samples.Tests
{
    public class Sample04_Catalog
    {
        [Fact]
        public async Task GetsAllPackageIds()
        {
            var httpClient = new HttpClient();

            var clientFactory = new NuGetClientFactory(httpClient, "https://api.nuget.org/v3/index.json");
            var catalogClient = await clientFactory.CreateCatalogClientAsync();

            var catalogProcessor =  new CatalogProcessor(
                new NullCursor(),
                catalogClient,
                leafProcessor: null,
                new CatalogProcessorOptions
                {
                },
                null);

            var catalogIndex = await catalogClient.GetIndexAsync();
            var catalogPageItems = new ConcurrentBag<CatalogPageItem>(catalogIndex.Items);
            var results = new ConcurrentBag<PackageIdentity>();

            var tasks = Enumerable
                .Range(0, 32)
                .Select(async _ =>
                {
                    while (catalogPageItems.TryTake(out var pageItem))
                    {
                        var catalogPage = await catalogClient.GetPageAsync(pageItem.Url);
                        foreach (var catalogLeafItem in catalogPage.Items)
                        {
                            results.Add(new PackageIdentity(catalogLeafItem.PackageId, catalogLeafItem.ParsePackageVersion()));
                        }
                    }
                });

            foreach (var package in results.Distinct())
            {
                Console.WriteLine($"{package.Id} {package.Version}");
            }
        }
    }
}
