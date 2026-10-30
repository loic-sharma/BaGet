# Extension.BaGet.Catalog

The NuGet V3 [catalog](https://learn.microsoft.com/nuget/api/catalog-resource) of
[BaGet Extension](https://pieroviano.github.io/BaGet/): an append-only log of package changes, which mirrors follow.

```powershell
dotnet add package Extension.BaGet.Catalog
```

## Usage

`AddBaGetWebApplication` (`Extension.BaGet.Web`) registers it; standalone: `services.AddBaGetCatalog()`.

| Item | Role |
|---|---|
| `CatalogWriter` | `IPackageChangeHandler`: appends a catalog entry for every package change |
| `CatalogController`, `CatalogReader` | Serve `v3/catalog/index.json`, `v3/catalog/page{n}.json`, `v3/catalog/data/{commitId}.json` |
| `CatalogRebuilder` | Appends missing entries; run by the host's `catalog rebuild [-r <repo>]` command or from `/admin/operations` (`CatalogRebuildOperation`); the catalog is per repository |
| `CatalogServiceIndexProvider` | Advertises the `Catalog/3.0.0` resource in the service index |

A database without the catalog table (Azure Table storage) gets `NullCatalogDatabase`: the catalog is off.

To read a catalog, use `CatalogProcessor` in `Extension.BaGet.Protocol`.

## Layout

One type per file, namespace = `BaGet.Catalog` + folder (`CodeLayoutTests`): `Controllers/Protocols` (`CatalogController`), `Database` (`ICatalogDatabase`, `CatalogDatabase`, `NullCatalogDatabase`), `Storage` (`CatalogReader`, `CatalogWriter`), `Rebuild` (`CatalogRebuilder`, `CatalogRebuildOperation`), `ServiceIndex`, `Extensions` (`AddBaGetCatalog`).

## Links

[Documentation](https://pieroviano.github.io/BaGet/) · [Source](https://github.com/pieroviano/BaGet)
