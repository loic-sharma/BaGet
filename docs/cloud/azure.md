# Running BaGet on Azure

!!! warning
    This page is a work in progress!

Use Azure to scale BaGet to nuget.org scale. You can configure BaGet to store metadata on [Azure SQL Database](https://azure.microsoft.com/en-us/services/sql-database/), upload packages to [Azure Blob Storage](https://azure.microsoft.com/en-us/services/storage/blobs/), and provide powerful search using [Azure Search](https://azure.microsoft.com/en-us/services/search/).

## Azure SQL Database

Update the [`appsettings.json`](https://github.com/loic-sharma/BaGet/blob/master/src/BaGet/appsettings.json) file:

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

## Azure Blob Storage

Update the [`appsettings.json`](https://github.com/loic-sharma/BaGet/blob/master/src/BaGet/appsettings.json) file:

```json
{
    ...

    "Storage": {
        "Type": "AzureBlobStorage",
        "AccountName": "my-account",
        "Container": "my-container",
        "AccessKey": "abcd1234"
    },

    ...
}
```

## Azure Search

Update the [`appsettings.json`](https://github.com/loic-sharma/BaGet/blob/master/src/BaGet/appsettings.json) file:

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

## TODO

* App Service
* High availibility setup