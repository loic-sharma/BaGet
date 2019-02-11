# Configuration

You can modify BaGet's configurations by editing the [`appsettings.json`](https://github.com/loic-sharma/BaGet/blob/master/src/BaGet/appsettings.json) file.

## Requiring an API key

You can require that users provide a password, called an API key, to publish packages.
To do so, you can insert the desired API key in the `ApiKey` field.

```json
{
    "ApiKey": "NUGET-SERVER-API-KEY",
    ...
}
```

Users will now have to provide the API key to push packages:

```c#
dotnet nuget push -s http://localhost:5000/v3/index.json -k NUGET-SERVER-API-KEY newtonsoft.json.11.0.2.nupkg
```

## Enabling Read-Through Caching

Read-through caching lets you index packages from an upstream source. You can use read-through
caching to:

1. Speed up your builds if restores from [nuget.org](https://nuget.org) are slow
2. Enable package restores in offline scenarios

The following `Mirror` settings configures BaGet to index packages from [nuget.org](https://nuget.org):

```json
{
    ...

    "Mirror": {
        "Enabled":  true,
        "PackageSource": "https://api.nuget.org/v3/index.json"
    },

    ...
}
```

!!! info
    `PackageSource` is the value of the [NuGet service index](https://docs.microsoft.com/en-us/nuget/api/service-index).

## Enabling Package Hard Deletions

To prevent the ["left pad" problem](https://blog.npmjs.org/post/141577284765/kik-left-pad-and-npm),
BaGet's default configuration doesn't allow package deletions. Whenever BaGet receives a package deletion
request, it will instead "unlist" the package. An unlisted package is undiscoverable but can still be
downloaded if you know the package's id and version. You can override this behavior by setting the
`PackageDeletionBehavior`:

```json
{
    ...

    "PackageDeletionBehavior": "HardDelete",

    ...
}
```

## Enabling Package Overwrites

Normally, pushing a package with an already existing id and version will be rejected. You can configure BaGet
to instead overwrite the existing package by setting `AllowPackageOverwrites`:

```json
{
    ...

    "AllowPackageOverwrites": true,

    ...
}
```
