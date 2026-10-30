# Import nuget.org packages

## Mirroring

You can configure BaGet Extension to mirror nuget.org. For example, say you install BaGet Extension, enable mirroring, and try to install the package
[`Newtonsoft.Json`](https://www.nuget.org/packages/Newtonsoft.Json/). BaGet Extension doesn't have this package yet, so it will
automatically index this package from nuget.org. This is also known as "read-through caching".

For more information, please see [Enable read-through caching](../configuration.md#enable-read-through-caching). `Mirror:Legacy` reads a NuGet v2 source and `Mirror:PackageDownloadTimeoutSeconds` (default 600) is the timeout of every HTTP request to the upstream.

## Importing package downloads from nuget.org

You can import nuget.org's download counts for the packages BaGet Extension already has:

1. Navigate to `.\BaGet\src\BaGet`
2. Run:

```
dotnet run -p:GeneratePackageOnBuild=false -- import downloads
```

Or, from a release: `dotnet BaGet.dll import downloads`.

It runs against the configured `Database`, overwrites the stored download count of every package version nuget.org lists, and needs a relational database (not Azure Table).

## Importing all nuget.org packages

Not supported as a command. Use read-through mirroring (above), which indexes packages on demand.
