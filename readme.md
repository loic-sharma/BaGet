# BaGet Extension :baguette_bread:

![Build status]

A [NuGet], npm, Maven and Docker registry and [symbol] server.

<p align="center">
  <img width="100%" src="docs/BaGetSite.png">
</p>

## Getting Started

1. Install the [.NET SDK] (BaGet Extension targets .NET 10)
2. Download the `BaGet` artifact of a [Release workflow] run, or build it from source (see [CONTRIBUTING.md](CONTRIBUTING.md))
3. Start the service with `dotnet BaGet.dll`
4. Browse `http://localhost:6060/` in your browser (the port is set by `Kestrel:Endpoints` in `appsettings.json`)
5. Sign in as the default administrator `Admin` / `Admin1234!` (created on a fresh install) and change that password at once ([details](docs/configuration.md#default-administrator))

For more information, please refer to the [documentation]. To build and test from source (nuget.config lists the untracked `Packages` folder as a package source, so create it once):

```
mkdir Packages
dotnet build BaGet.sln -p:GeneratePackageOnBuild=false
dotnet test BaGet.sln -p:GeneratePackageOnBuild=false
```

The screenshot above shows the upstream project's UI, not the current one.

## Features

* **Cross-platform**: runs on Windows, macOS, and Linux!
* **Cloud native**: runs in [Docker], [Azure], [AWS], [Google Cloud], [Alibaba Cloud]
* **Four databases**: SQLite, SQL Server, PostgreSQL and MySQL, plus Azure Table Storage
* **Repositories and formats**: several [repositories] per server (`/r/{name}/`), each NuGet, [npm], [Maven] or [Docker registry] (OCI)
* **Remote and virtual repositories**: [cache an upstream] (NuGet, npm, Maven, Docker; offline mode, purge) and [merge several repositories] behind one URL
* **Offline support**: [mirror a NuGet server] to speed up builds and enable offline downloads
* **NuGet V3 protocol**, including the catalog, vulnerability info, deprecation, report abuse and package details templates
* **NuGet V2 (OData) feed** for `nuget list`, older clients and PowerShellGet (on by default; `Feeds:EnableV2=false` turns it off)
* **Accounts** (on by default; `Accounts:Enabled=false` restores the global `ApiKey` alone; while `ApiKey` is empty, pushes need a user API key): sign-in, OpenID Connect, package owners, organizations and scoped, expiring API keys
* **Access control**: groups, permission targets, project roles, private repositories and an audit log ([access control])
* **Validation pipeline** (opt-in): packages are checked before they are served, and can be repository signed
* **Retention**, **vulnerability scanning** (OSV.dev, Trivy; opt-in) and **replication** between BaGet servers: [retention], [scanning], [replication]
* **High availability** (opt-in): several nodes on one database, object store and Redis ([high availability])
* **Administration REST API** (`/api/v1`, OpenAPI 3) and admin pages for everything the API does: [admin API]
* **OpenTelemetry export** (traces, metrics, logs; needs an OTLP endpoint) and a live Settings page
* **Web UI**: package pages (with "Open in NuGet Package Explorer", web and desktop), owner management pages, profiles, statistics and abuse reports

[Build status]: https://img.shields.io/github/actions/workflow/status/pieroviano/BaGet/main.yml?branch=main

[NuGet]: https://learn.microsoft.com/nuget/what-is-nuget
[symbol]: https://docs.microsoft.com/en-us/windows/desktop/debug/symbol-servers-and-symbol-stores
[.NET SDK]: https://www.microsoft.com/net/download

[Release workflow]: https://github.com/pieroviano/BaGet/actions/workflows/release.yml

[Documentation]: docs/index.md
[Docker]: docs/installation/docker.md
[Azure]: docs/installation/azure.md
[AWS]: docs/installation/aws.md
[Google Cloud]: docs/installation/gcp.md
[Alibaba Cloud]: docs/installation/aliyun.md

[Mirror a NuGet server]: docs/configuration.md#enable-read-through-caching
[Repositories]: docs/repositories.md
[npm]: docs/npm-registry.md
[Maven]: docs/maven-repositories.md
[Cache an upstream]: docs/remote-repositories.md
[Merge several repositories]: docs/virtual-repositories.md
[Access control]: docs/access-control.md
[Retention]: docs/retention.md
[Scanning]: docs/vulnerability-scanning.md
[Replication]: docs/replication.md
[High availability]: docs/high-availability.md
[Admin API]: docs/admin-api.md
[Docker registry]: docs/docker-registry.md
