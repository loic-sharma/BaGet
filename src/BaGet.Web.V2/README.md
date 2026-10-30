# Extension.BaGet.Web.V2

The legacy NuGet V2 (OData/Atom) feed of [BaGet Extension](https://pieroviano.github.io/BaGet/), for clients that
cannot use V3.

```powershell
dotnet add package Extension.BaGet.Web.V2
```

## Usage

`AddBaGetWebApplication` (`Extension.BaGet.Web`) registers `V2FeedController` as an application part and
`BaGetEndpointBuilder` maps its read-only `GET` routes under `api/v2`: the root, `$metadata`, `Packages()`,
`Packages(Id=..,Version=..)`, `FindPackagesById()` and `Search()`. `$metadata` is the EDMX (OData 2.0) model: entity type
`NuGetGallery.V2FeedPackage` (media link entries), entity set `Packages` and the `Search` / `FindPackagesById` service operations. Push, delete and symbol upload (`PUT`/`DELETE`
on `api/v2/package` and `api/v2/symbol`) are in `Extension.BaGet.Web`. The routes are always mapped; with the feed
off every action answers 404.

| Setting | Value |
|---|---|
| `Feeds:EnableV2` | Default `true`; `false` turns the feed off (404) |

Client source: `https://<server>/api/v2`.

## Links

[Documentation](https://pieroviano.github.io/BaGet/) · [Source](https://github.com/pieroviano/BaGet)
