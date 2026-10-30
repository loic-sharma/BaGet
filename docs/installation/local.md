# Run BaGet Extension on your Computer

## Run BaGet Extension

1. Install the [.NET 10 ASP.NET Core Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (or the SDK)
1. Get BaGet Extension: download the `BaGet` workflow artifact of a release run of this repository, or publish it with `dotnet publish src/BaGet -c Release -o artifacts -p:GeneratePackageOnBuild=false`, and extract it
1. Start the service with `dotnet BaGet.dll`
1. Browse `http://localhost:6060/` in your browser

!!! info
    On a server with no users, the first start creates the administrator `Admin` / `Admin1234!` (`Accounts:CreateDefaultAdministrator`); change that password at once. See [Default administrator](../configuration.md#default-administrator).

!!! info
    The port comes from `Kestrel:Endpoints` in `appsettings.json`, which takes precedence over
    `--urls`. Change it there, or override it with the `Kestrel__Endpoints__Http__Url` environment variable
    (for example `http://localhost:8080`).

## Configure BaGet Extension

You can modify BaGet Extension's configurations by editing the `appsettings.json` file. Database migrations run at startup (`RunMigrationsAtStartup`, on by default). For the full list of configurations, please refer to [BaGet Extension's configuration](../configuration.md) guide.

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
