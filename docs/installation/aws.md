# Run BaGet on AWS

!!! warning
    This page is a work in progress!

Use Amazon Web Services to scale BaGet. You can store metadata on [Amazon RDS](https://aws.amazon.com/rds/postgresql/) and upload packages to [Amazon S3](https://aws.amazon.com/s3/).

## Configure BaGet

You can modify BaGet's configurations by editing the `appsettings.json` file. For the full list of configurations, please refer to [BaGet's configuration](../configuration.md) guide.

### Amazon S3

Update the `appsettings.json` file:

```json
{
    ...

    "Storage": {
        "Type": "AwsS3",
        "Region": "us-west-1",
        "Bucket": "foo",
        "AccessKey": "",
        "SecretKey": ""
    },

    ...
}
```

### Amazon RDS

To use PostgreSQL, update the `appsettings.json` file:

```json
{
    ...

    "Database": {
        "Type": "PostgreSql",
        "ConnectionString": "..."
    },

    ...
}
```

To use MySQL, update the `appsettings.json` file:

```json
{
    ...

    "Database": {
        "Type": "MySql",
        "ConnectionString": "..."
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
