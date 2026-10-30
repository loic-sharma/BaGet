using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Samples.Tests;

public class Sample02_Search
{
    [Fact]
    public async Task Exists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Check if a package exists.
        NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");

        if (!await client.ExistsAsync("newtonsoft.json", cancellationToken))
        {
            Console.WriteLine("Package 'newtonsoft.json' does not exist!");
        }

        var packageVersion = NuGetVersion.Parse("12.0.1");
        if (!await client.ExistsAsync("newtonsoft.json", packageVersion, cancellationToken))
        {
            Console.WriteLine("Package 'newtonsoft.json' version '12.0.1' does not exist!");
        }
    }

    [Fact]
    public async Task Search()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Search for packages that are relevant to "json".
        NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");
        IReadOnlyList<SearchResult> results = await client.SearchAsync("json", cancellationToken);

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
        var cancellationToken = TestContext.Current.CancellationToken;
        // Search for packages whose names' start with "Newt".
        NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");
        IReadOnlyList<string> packageIds = await client.AutocompleteAsync("Newt", cancellationToken);

        foreach (string packageId in packageIds)
        {
            Console.WriteLine($"Found package ID '{packageId}'");
        }
    }
}