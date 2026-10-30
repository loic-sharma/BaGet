# Extension.BaGet.Protocol

BaGet Extension's SDK to interact with the [NuGet protocol](https://learn.microsoft.com/nuget/api/overview).
Standalone: it does not depend on the rest of BaGet.

[Documentation](https://pieroviano.github.io/BaGet/advanced/sdk/) | [Samples](https://github.com/pieroviano/BaGet/tree/main/samples) | [Source](https://github.com/pieroviano/BaGet)

## Getting started

### Install the package

```powershell
dotnet add package Extension.BaGet.Protocol
```

## Examples

### List Package Versions

Find all versions of the `Newtonsoft.Json` package:

```csharp
NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");

IReadOnlyList<NuGetVersion> versions = await client.ListPackageVersionsAsync("Newtonsoft.Json");

foreach (NuGetVersion version in versions)
{
    Console.WriteLine($"Found version: {version}");
}
```

### Download a package

```csharp
NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");

string packageId = "Newtonsoft.Json";
NuGetVersion packageVersion = new NuGetVersion("12.0.1");

using (Stream packageStream = await client.DownloadPackageAsync(packageId, packageVersion))
{
    Console.WriteLine($"Downloaded package {packageId} {packageVersion}");
}
```

### Find Package Metadata

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
    Console.WriteLine($"Tags: {metadata.Tags}");
    Console.WriteLine($"Description: {metadata.Description}");
}

// Or, find the metadata for a single version of a package.
string packageId = "Newtonsoft.Json";
NuGetVersion packageVersion = new NuGetVersion("12.0.1");

PackageMetadata? metadata = await client.GetPackageMetadataAsync(packageId, packageVersion);
if (metadata == null) return; // the version does not exist

Console.WriteLine($"Listed: {metadata.Listed}");
Console.WriteLine($"Tags: {metadata.Tags}");
Console.WriteLine($"Description: {metadata.Description}");
```

### Search for packages

Search for "json" packages:

```csharp
NuGetClient client = new NuGetClient("https://api.nuget.org/v3/index.json");
IReadOnlyList<SearchResult>? results = await client.SearchAsync("json");

foreach (SearchResult result in results ?? [])
{
    Console.WriteLine($"Found package {result.PackageId} {result.Version}");
}
```

### Read a catalog

A mirror follows a server's [catalog](https://learn.microsoft.com/nuget/api/catalog-resource) with
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

### Multiple search endpoints

When a server's service index lists several endpoints for search or autocomplete, as nuget.org's
does, the client tries them in order and moves to the next on a network failure, a timeout, a 408,
a 429, a 5xx or an unreadable response. Any other status, such as a 404, is the server's answer and
is returned as is. The service index is re-read after 40 minutes.
