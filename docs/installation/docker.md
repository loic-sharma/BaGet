# Run BaGet Extension on Docker

## Configure BaGet Extension

Create a file named `baget.env` to store BaGet Extension's configurations:

```
# The global API Key used to publish packages. NUGET-SERVER-API-KEY is only an example: change it
# to a secret value. With accounts on, users can also push with their own keys (/account/apikeys).
ApiKey=NUGET-SERVER-API-KEY

# Listen on every interface inside the container; appsettings.json binds to localhost:6060.
Kestrel__Endpoints__Http__Url=http://+:80

Storage__Type=FileSystem
Storage__Path=/var/baget/packages
Database__Type=Sqlite
Database__ConnectionString=Data Source=/var/baget/baget.db
Search__Type=Database

# Where the server's Settings page saves the settings it changes. Keep it in the mounted volume,
# otherwise a recreated container loses them.
SettingsOverridesPath=/var/baget/appsettings.Overrides.json

# Logs go to the console (docker logs) and to a rolling file; the default, /app/logs, is lost with the
# container, so keep the files in the volume.
Serilog__WriteTo__File__Args__path=/var/baget/logs/baget-.log
```

For a full list of configurations, please refer to [BaGet Extension's configuration](../configuration.md) guide.

!!! info
    The `baget.env` file stores [BaGet Extension's configuration](../configuration.md) as environment
    variables. To learn how these configurations work, please refer to
    [ASP.NET Core's configuration documentation](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0&tabs=basicconfiguration#configuration-by-environment).

## Run BaGet Extension

1. Create a folder named `baget-data` in the same directory as the `baget.env` file. This will be used by BaGet Extension to persist its state.
2. Get BaGet Extension's docker image: pull the image this fork's release workflow publishes (the `DOCKER_IMAGE` repository variable, else `<DOCKER_USERNAME>/baget`), or build it from the repository root:

```
docker build -t baget .
```

The build context is the repository root: the image needs `Directory.Build.props` and `SignKey.snk` (strong naming) next to `src/`. `.dockerignore` keeps out everything else, including the local data of a developer run (`src/BaGet/appsettings.Overrides.json`, `baget.db`, `packages/`...).

You can now run BaGet Extension:

```
docker run --rm --name nuget-server -p 6060:80 --env-file baget.env -v "$(pwd)/baget-data:/var/baget" baget:latest   # or <image>:latest for the pulled image
```

## Publish packages

Publish your first package with:

```
dotnet nuget push -s http://localhost:6060/v3/index.json -k NUGET-SERVER-API-KEY package.1.0.0.nupkg
```

Publish your first [symbol package](https://docs.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg) with:

```
dotnet nuget push -s http://localhost:6060/v3/index.json -k NUGET-SERVER-API-KEY symbol.package.1.0.0.snupkg
```

!!! warning
    `NUGET-SERVER-API-KEY` is only the example value in `baget.env`: change it to a secret value. With an empty `ApiKey` and [accounts](../configuration.md#accounts-package-owners-and-api-keys) on, a push needs a user API key from `/account/apikeys`. See [Configure BaGet Extension](#configure-baget-extension).

!!! info
    On a server with no users, the first start creates the administrator `Admin` / `Admin1234!` (`Accounts:CreateDefaultAdministrator`); change that password at once. See [Default administrator](../configuration.md#default-administrator).

## Tracing with Jaeger

To see each request, push and database call as a trace, run [Jaeger](https://www.jaegertracing.io) next to BaGet Extension and
point it at Jaeger's OTLP port (see [Telemetry](../configuration.md#telemetry)):

```yaml
services:
  baget:
    image: <your BaGet Extension image>
    ports:
      - "6060:80"
    environment:
      - Telemetry__Enabled=true
      - Telemetry__Otlp__Endpoint=http://jaeger:4317
    depends_on:
      - jaeger

  jaeger:
    image: jaegertracing/all-in-one:latest
    ports:
      - "16686:16686"   # UI: http://localhost:16686
    environment:
      - COLLECTOR_OTLP_ENABLED=true
```

Jaeger takes traces only: for metrics and logs set `Telemetry__Metrics__Endpoint` / `Telemetry__Logs__Endpoint` to an
OpenTelemetry Collector instead. The same keys can be changed live on the Settings page.

## Browse packages

You can browse packages by opening the URL [`http://localhost:6060/`](http://localhost:6060/) in your browser.

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
