# BaGet.Protocol

BaGet's SDK to interact with a NuGet server.

[Package (NuGet)](https://www.nuget.org/packages/BaGet.Protocol) | Documentation | [Samples](https://github.com/loic-sharma/BaGet/tree/master/samples)

## Getting started

### Install the package

```powershell
dotnet add package BaGet.Protocol
```

## Examples

### Downloading a package

```csharp
using BaGet.Protocol;
using NuGet.Versioning;

var clientFactory = new NuGetClientFactory("https://api.nuget.org/v3/index.json");
var contentClient = clientFactory.CreatePackageContentClient();

var packageId = "Newtonsoft.Json";
var packageVersion = new NuGetVersion("12.0.1");

using (var packageStream = await contentClient.GetPackageContentStreamOrNullAsync(packageId, packageVersion))
{
    if (packageStream == null)
    {
        Console.WriteLine($"Package {packageId} {packageVersion} does not exist");
        return;
    }

    Console.WriteLine($"Downloaded package {packageId} {packageVersion}");
}
```

### Search for packages

```csharp
// Searches for "json" packages, including prerelease packages.
var clientFactory = new NuGetClientFactory("https://api.nuget.org/v3/index.json");
var searchClient = clientFactory.CreateSearchClient();

var response = await searchClient.SearchAsync(new SearchRequest
{
    Query = "json",
    IncludePrerelease = true,
});

Console.WriteLine($"Found {response.TotalHits} total results for query {searchRequest.Query}");

foreach (var searchResult in response.Data)
{
    Console.WriteLine($"Found package {searchResult.Id} {searchResult.Version}");
}
```