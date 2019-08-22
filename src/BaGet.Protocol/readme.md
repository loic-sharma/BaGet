# BaGet.Protocol

BaGet's SDK to interact with the [NuGet protocol](https://docs.microsoft.com/en-us/nuget/api/overview).

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

var client = new NuGetClient("https://api.nuget.org/v3/index.json");

var packageId = "Newtonsoft.Json";
var packageVersion = new NuGetVersion("12.0.1");

using (var packageStream = await client.GetPackageStreamAsync(packageId, packageVersion))
{
    Console.WriteLine($"Downloaded package {packageId} {packageVersion}");
}
```

### Search for packages

```csharp
// Searches for "json" packages, including prerelease packages.
var client = new NuGetClient("https://api.nuget.org/v3/index.json");
var response = await client.SearchAsync("json");

Console.WriteLine($"Found {response.TotalHits} total results");

foreach (var searchResult in response.Data)
{
    Console.WriteLine($"Found package {searchResult.Id} {searchResult.Version}");
}
```
