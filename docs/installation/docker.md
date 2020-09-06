# Run BaGet on Docker

## Configure BaGet

Create a file named `baget.env` to store BaGet's configurations:

```
# The following config is the API Key used to publish packages.
# You should change this to a secret value to secure your server.
ApiKey=NUGET-SERVER-API-KEY

Storage__Type=FileSystem
Storage__Path=/var/baget/packages
Database__Type=Sqlite
Database__ConnectionString=Data Source=/var/baget/baget.db
Search__Type=Database
```

For a full list of configurations, please refer to [BaGet's configuration](../configuration.md) guide.

!!! info
    The `baget.env` file stores [BaGet's configuration](configuration) as environment
    variables. To learn how these configurations work, please refer to
    [ASP.NET Core's configuration documentation](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-2.1&tabs=basicconfiguration#configuration-by-environment).

## Run BaGet

1. Create a folder named `baget-data` in the same directory as the `baget.env` file. This will be used by BaGet to persist its state.
2. Pull BaGet's latest [docker image](https://hub.docker.com/r/loicsharma/baget):

```
docker pull loicsharma/baget
```

You can now run BaGet:

```
docker run --rm --name nuget-server -p 5555:80 --env-file baget.env -v "$(pwd)/baget-data:/var/baget" loicsharma/baget:latest
```

## Publish packages

Publish your first package with:

```
dotnet nuget push -s http://localhost:5555/v3/index.json -k NUGET-SERVER-API-KEY package.1.0.0.nupkg
```

Publish your first [symbol package](https://docs.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg) with:

```
dotnet nuget push -s http://localhost:5555/v3/index.json -k NUGET-SERVER-API-KEY symbol.package.1.0.0.snupkg
```

!!! warning
    The default API Key to publish packages is `NUGET-SERVER-API-KEY`. You should change this to a secret value to secure your server. See [Configure BaGet](#configure-baget).

## Browse packages

You can browse packages by opening the URL [`http://localhost:5555/`](http://localhost:5555/) in your browser.

## Restore packages

You can restore packages by using the following package source:

`http://localhost:5555/v3/index.json`

Some helpful guides:

* [Visual Studio](https://docs.microsoft.com/en-us/nuget/consume-packages/install-use-packages-visual-studio#package-sources)
* [NuGet.config](https://docs.microsoft.com/en-us/nuget/reference/nuget-config-file#package-source-sections)

## Symbol server

You can load symbols by using the following symbol location:

`http://localhost:5555/api/download/symbols`

For Visual Studio, please refer to the [Configure Debugging](https://docs.microsoft.com/en-us/visualstudio/debugger/specify-symbol-dot-pdb-and-source-files-in-the-visual-studio-debugger?view=vs-2017#configure-symbol-locations-and-loading-options) guide.
