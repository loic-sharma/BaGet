# Run BaGet Extension on AWS

Use Amazon Web Services to scale BaGet Extension. You can store metadata on [Amazon RDS](https://aws.amazon.com/rds/postgresql/) and upload packages to [Amazon S3](https://aws.amazon.com/s3/).

## Configure BaGet Extension

You can modify BaGet Extension's configurations by editing the `appsettings.json` file. For the full list of configurations, please refer to [BaGet Extension's configuration](../configuration.md) guide.

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

`Region` and `Bucket` are required; set `AccessKey` and `SecretKey` together, or leave both empty to use the default AWS credential chain. Optional keys:

| Key | Meaning |
| --- | --- |
| `Prefix` | Key prefix for objects in the bucket |
| `UseInstanceProfile` | Use the default AWS credential chain (environment, profile, then EC2 instance profile), ignoring `AccessKey`/`SecretKey` and `AssumeRoleArn` |
| `AssumeRoleArn` | Role to assume, using the default credential chain as the caller; `AccessKey`/`SecretKey` are ignored |

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
