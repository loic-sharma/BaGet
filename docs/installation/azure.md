# Run BaGet Extension on Azure

Use Azure to scale BaGet Extension. You can store metadata on [Azure SQL Database](https://azure.microsoft.com/en-us/services/sql-database/), upload packages to [Azure Blob Storage](https://azure.microsoft.com/en-us/services/storage/blobs/), and provide powerful search using [Azure Search](https://azure.microsoft.com/en-us/services/search/).

## TODO

* App Service
* High availability setup

## Configure BaGet Extension

You can modify BaGet Extension's configurations by editing the `appsettings.json` file. For the full list of configurations, please refer to [BaGet Extension's configuration](../configuration.md) guide.

### Azure SQL database

Update the `appsettings.json` file:

```json
{
    ...

    "Database": {
        "Type": "SqlServer",
        "ConnectionString": "..."
    },

    ...
}
```

### Azure Blob Storage

Update the `appsettings.json` file:

```json
{
    ...

    "Storage": {
        "Type": "AzureBlobStorage",

        "AccountName": "my-account",
        "AccessKey": "abcd1234",
        "Container": "my-container"
    },

    ...
}
```

Alternatively, you can use a full Azure Storage connection string:

```json
{
    ...

    "Storage": {
        "Type": "AzureBlobStorage",

        "ConnectionString": "AccountName=my-account;AccountKey=abcd1234;...",
        "Container": "my-container"
    },

    ...
}
```

Package downloads are redirected to the blob through a shared access signature, valid for 10
minutes by default. To change it, set `DownloadUriExpiry` in the `Storage` section, as a time span
such as `"00:30:00"`.

### Azure Table Storage

Set `"Database": { "Type": "AzureTable", "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net" }` to store package metadata in Table Storage. It only
stores packages: accounts, the catalog, validation, statistics, abuse reports, deprecation and
vulnerabilities need Azure SQL Database or another relational database. With it, `Search:Type` `Database`
is served by the table provider.

### Azure Search

The index needs a `normalizedId` field: searchable, filterable, analyzed with BaGet Extension's exact match
analyzer (`PackageDocument` declares it). Autocomplete and version listing use it. Documents
indexed before the field existed keep working through a fallback, and gain the field the next
time a version of their package is pushed.

Update the `appsettings.json` file:

```json
{
    ...

    "Search": {
        "Type": "AzureSearch",
        "AccountName": "my-account",
        "ApiKey": "ABCD1234"
    },

    ...
}
```

## Publish packages

Publish your first package with:

```
dotnet nuget push -s http://localhost:6060/v3/index.json -k <your API key> package.1.0.0.nupkg
```

Publish your first [symbol package](https://docs.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg) with:

```
dotnet nuget push -s http://localhost:6060/v3/index.json -k <your API key> symbol.package.1.0.0.snupkg
```

!!! warning
    With [accounts](../configuration.md#accounts-package-owners-and-api-keys) on (the default) and an empty `ApiKey`, a push without a key is refused with `401`. Pass either a user API key (created on `/account/apikeys`) or a non-empty global `ApiKey`, with `-k <key>`. See also [Require an API key](../configuration.md#require-an-api-key).

## Restore packages

You can restore packages by using the following package source:

`http://localhost:6060/v3/index.json`

Some helpful guides:

* [Visual Studio](https://docs.microsoft.com/en-us/nuget/consume-packages/install-use-packages-visual-studio#package-sources)
* [NuGet.config](https://docs.microsoft.com/en-us/nuget/reference/nuget-config-file#package-source-sections)

## Symbol server

You can load symbols by using the following symbol location:

`http://localhost:6060/api/download/symbols`

For Visual Studio, please refer to the [Configure Debugging](https://docs.microsoft.com/en-us/visualstudio/debugger/specify-symbol-dot-pdb-and-source-files-in-the-visual-studio-debugger?view=vs-2017#configure-symbol-locations-and-loading-options) guide.
