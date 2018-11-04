# Configuration

You can modify BaGet's configurations by editing the [`appsettings.json`](https://github.com/loic-sharma/BaGet/blob/master/src/BaGet/appsettings.json) file.

## Requiring an API key

You can require that users provide a password, called an API key, to publish packages.
To do so, insert the SHA-256 hash of your desired API key in the `ApiKeyHash` field:

```json
{
    "ApiKeyHash": "658489D79E218D2474D049E8729198D86DB0A4AF43981686A31C7DCB02DC0900",

    ...
}
```

!!! info
    `658489D79E218D2474D049E8729198D86DB0A4AF43981686A31C7DCB02DC0900` is the SHA-256 hash of `NUGET-SERVER-API-KEY`.

Users will now have to provide the API key to push packages:

```
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
        "PackageSource": "https://api.nuget.org/v3-flatcontainer/"
    },

    ...
}
```

!!! info
    `PackageSource` is the value of the [`PackageBaseAddress`](https://docs.microsoft.com/en-us/nuget/api/overview#resources-and-schema) resource
    on a [NuGet service index](https://docs.microsoft.com/en-us/nuget/api/service-index).

## Enabling Package Hard Deletions

To prevent the ["left pad" problem](https://blog.npmjs.org/post/141577284765/kik-left-pad-and-npm), BaGet's default configuration doesn't allow package deletions. Whenever BaGet receives a package deletion request, it will instead "unlist" the package. An unlisted package is undiscoverable but can still be downloaded if you know the package's id and version. You can override this behavior by setting the `PackageDeletionBehavior`:

```json
{
    ...

    "PackageDeletionBehavior": "HardDelete",

    ...
}
```
