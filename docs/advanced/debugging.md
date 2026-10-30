# Debugging BaGet Extension

## Run

Every project is strong-name signed with `SignKey.snk` at the repository root; the build fails without it.

In a fresh clone create the `Packages` folder at the repository root first: nuget.config lists the untracked `Packages` folder as a package source, and restore fails with NU1301 without it.

```
cd src/BaGet
dotnet run -p:GeneratePackageOnBuild=false
```

`src/Directory.Build.props` packs on every build into the shared `Packages` folder; the flag stops that.
`dotnet run` may rewrite `src/BaGet/Properties/launchSettings.json` (it drops the IIS Express profile):
revert it rather than committing it.

## Build and test

```
dotnet build BaGet.sln -p:GeneratePackageOnBuild=false
dotnet test BaGet.sln -p:GeneratePackageOnBuild=false
dotnet test tests/BaGet.Tests/BaGet.Tests.csproj -p:GeneratePackageOnBuild=false --filter-class "*UploadPageTests"
```

Tests need no external services (`BaGet.Database.Tests` uses Docker via Testcontainers and skips its 20 tests without a daemon; `BaGet.Telemetry.Tests`, `BaGet.Storage.Tests`, `BaGet.Clients.Tests` and `BaGet.Ha.Tests` do the same with their containers). `tests/BaGet.Tests/Support/BaGetApplication.cs` runs the real pipeline
over HTTP with SQLite in a temporary directory; it supplies settings with `builder.UseSetting(...)`.
Tests are xUnit v3 on Microsoft.Testing.Platform (opted in by `global.json`); filter with `--filter-class` /
`--filter-method`, which take `*` wildcards.

## Port

BaGet Extension listens on `http://localhost:6060`. `Kestrel:Endpoints:Http:Url` in `appsettings.json` outranks
`--urls` and `ASPNETCORE_URLS`, so those have no effect. Override it with the environment variable
`Kestrel__Endpoints__Http__Url`.

## Logging

Serilog, configured by the `Serilog` section of `appsettings.json` (see [Logging](../configuration.md#logging)).
Development already logs at `Debug`. Raise a namespace, for example:

```json
{ "Serilog": { "MinimumLevel": { "Override": { "BaGet": "Debug" } } } }
```

Logs also go to `logs/baget-YYYYMMDD.log` under the content root. Level changes in a configuration file apply without a restart.

## Command line

| Command | Does |
| --- | --- |
| `dotnet BaGet.dll import downloads [-r <repo>]` | imports nuget.org download counts (into repository `<repo>`, default `default`) |
| `dotnet BaGet.dll catalog rebuild [-r <repo>]` | appends the catalog entries missing for existing packages of a repository (default `default`) |
| `dotnet BaGet.dll account reset-password <user> [--email <address>]` | sets a generated password, switches off two-factor, lifts lockout; needs `Accounts:Enabled` |
| `dotnet BaGet.dll account promote <user>` / `demote <user>` | sets or clears the administrator flag; needs `Accounts:Enabled`; the last administrator cannot be demoted |
