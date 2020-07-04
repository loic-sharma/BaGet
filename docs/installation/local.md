# Run BaGet on your Computer

## Run BaGet

1. Install the [.NET Core SDK](https://www.microsoft.com/net/download)
1. Download and extract [BaGet's latest release](https://github.com/loic-sharma/BaGet/releases)
1. Start the service with `dotnet BaGet.dll`
1. Browse `http://localhost:5000/` in your browser

## Configure BaGet

You can modify BaGet's configurations by editing the `appsettings.json` file. For the full list of configurations, please refer to [BaGet's configuration](../configuration.md) guide.

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
