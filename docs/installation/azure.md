# Run BaGet on Azure

!!! warning
    This page is a work in progress!

Use Azure to scale BaGet. You can store metadata on [Azure SQL Database](https://azure.microsoft.com/en-us/services/sql-database/), upload packages to [Azure Blob Storage](https://azure.microsoft.com/en-us/services/storage/blobs/), and provide powerful search using [Azure Search](https://azure.microsoft.com/en-us/services/search/).

## TODO

* App Service
* Table Storage
* High availability setup

## Configure BaGet

You can modify BaGet's configurations by editing the `appsettings.json` file. For the full list of configurations, please refer to [BaGet's configuration](../configuration.md) guide.

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

### Azure Search

Update the `appsettings.json` file:

```json
{
    ...

    "Search": {
        "Type": "Azure",
        "AccountName": "my-account",
        "ApiKey": "ABCD1234"
    },

    ...
}
```

## Publish packages

Publish your first package with:

```
dotnet nuget push -s http://localhost:5000/v3/index.json package.1.0.0.nupkg
```

Publish your first [symbol package](https://docs.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg) with:

```
dotnet nuget push -s http://localhost:5000/v3/index.json symbol.package.1.0.0.snupkg
```

!!! warning
    You should secure your server by requiring an API Key to publish packages. For more information, please refer to the [Require an API Key](../configuration.md#require-an-api-key) guide.

## Restore packages

You can restore packages by using the following package source:

`http://localhost:5000/v3/index.json`

Some helpful guides:

* [Visual Studio](https://docs.microsoft.com/en-us/nuget/consume-packages/install-use-packages-visual-studio#package-sources)
* [NuGet.config](https://docs.microsoft.com/en-us/nuget/reference/nuget-config-file#package-source-sections)

## Symbol server

You can load symbols by using the following symbol location:

`http://localhost:5000/api/download/symbols`

For Visual Studio, please refer to the [Configure Debugging](https://docs.microsoft.com/en-us/visualstudio/debugger/specify-symbol-dot-pdb-and-source-files-in-the-visual-studio-debugger?view=vs-2017#configure-symbol-locations-and-loading-options) guide.
