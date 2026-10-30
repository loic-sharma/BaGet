# BaGet Extension SDK

You can use BaGet Extension's [`BaGet.Protocol`](https://www.nuget.org/packages/BaGet.Protocol) package to interact with a NuGet server.

## Getting Started

Install the [`BaGet.Protocol`](https://www.nuget.org/packages/BaGet.Protocol) package:

```
dotnet add package BaGet.Protocol
```

## List Package Versions

Find the listed versions of the `Newtonsoft.Json` package:

```csharp
NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");

IReadOnlyList<NuGetVersion> versions = await client.ListPackageVersionsAsync("Newtonsoft.Json");

foreach (NuGetVersion version in versions)
{
    Console.WriteLine($"Found version: {version}");
}
```

## Download a package

```csharp
NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");

string packageId = "Newtonsoft.Json";
NuGetVersion packageVersion = new NuGetVersion("12.0.1");

using (Stream packageStream = await client.DownloadPackageAsync(packageId, packageVersion))
{
    Console.WriteLine($"Downloaded package {packageId} {packageVersion}");
}
```

## Find Package Metadata

```csharp
NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");

// Find the metadata for all versions of a package.
IReadOnlyList<PackageMetadata> items = await client.GetPackageMetadataAsync("Newtonsoft.Json");
if (!items.Any())
{
    Console.WriteLine($"Package 'Newtonsoft.Json' does not exist");
    return;
}

foreach (var metadata in items)
{
    Console.WriteLine($"Version: {metadata.Version}");
    Console.WriteLine($"Listed: {metadata.Listed}");
    Console.WriteLine($"Tags: {string.Join(", ", metadata.Tags ?? Array.Empty<string>())}");
    Console.WriteLine($"Description: {metadata.Description}");
}

// Or, find the metadata for a single version of a package
// (throws PackageNotFoundException when the package does not exist).
string packageId = "Newtonsoft.Json";
NuGetVersion packageVersion = new NuGetVersion("12.0.1");

PackageMetadata? single = await client.GetPackageMetadataAsync(packageId, packageVersion);

Console.WriteLine($"Listed: {single?.Listed}");
Console.WriteLine($"Description: {single?.Description}");
```

## Search for packages

Search for "json" packages:

```csharp
NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");
IReadOnlyList<SearchResult> results = await client.SearchAsync("json");

foreach (SearchResult result in results)
{
    Console.WriteLine($"Found package {result.PackageId} {result.Version}");
}
```

## Read a catalog

Types are in `BaGet.Protocol.Catalog`. A mirror follows a server's [catalog](https://learn.microsoft.com/nuget/api/catalog-resource) with
`CatalogProcessor`, which hands each new leaf to an `ICatalogLeafProcessor` you implement and
remembers how far it got in an `ICursor`:

```csharp
var httpClient = new HttpClient();
var factory = new NuGetClientFactory(httpClient, "https://api.nuget.org/v3/index.json");

var processor = new CatalogProcessor(
    new FileCursor("cursor.json", loggerFactory.CreateLogger<FileCursor>()),
    factory.CreateCatalogClient(),
    new MyLeafProcessor(),
    new CatalogProcessorOptions
    {
        DefaultMinCommitTimestamp = DateTimeOffset.MinValue,
        MaxCommitTimestamp = DateTimeOffset.MaxValue,
    },
    loggerFactory.CreateLogger<CatalogProcessor>());

await processor.ProcessAsync();
```

## Other calls

`NuGetClient` also has:

* `ExistsAsync(packageId)` and `ExistsAsync(packageId, version)`: whether a package or version exists
* `DownloadPackageManifestAsync(packageId, version)`: the `.nuspec`
* `ListPackageVersionsAsync(packageId, includeUnlisted)`: with `true`, also unlisted versions (from the package content resource)
* `AutocompleteAsync(...)`: package id suggestions (for version suggestions use `clientFactory.CreateAutocompleteClient().ListPackageVersionsAsync`)

`NuGetClientFactoryExtensions.CreateCatalogProcessor` is a shortcut for the wiring in
[Read a catalog](#read-a-catalog).

## Multiple search endpoints

When a server's service index lists several endpoints for search or autocomplete, as nuget.org's
does, the client tries them in order and moves to the next on a network failure, a timeout, a 408,
a 429, a 5xx, a dropped connection (`IOException`) or an unreadable response. Any other status, such as a 404, is the server's answer and
is returned as is. The service index is re-read after 40 minutes.
