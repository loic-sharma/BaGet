# Installation manual

Step-by-step installation of BaGet Extension. Per-platform details are in the linked pages.

## 1. Choose a platform

| Platform | Page |
|---|---|
| Your computer (Windows, Linux, macOS) | [On your computer](installation/local.md) |
| Docker | [Docker](installation/docker.md) |
| Windows, IIS | [IIS proxy](installation/iis-proxy.md) |
| Azure | [Azure](installation/azure.md) |
| AWS | [AWS](installation/aws.md) |
| Google Cloud | [Google Cloud](installation/gcp.md) |
| Alibaba Cloud | [Aliyun](installation/aliyun.md) |

## 2. Requirements

| Item | Needed for |
|---|---|
| [.NET 10 ASP.NET Core Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (or SDK) | Running on your computer |
| [.NET 10 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) | IIS |
| Docker | Docker install |
| A database (SQLite is built in) | SQL Server, PostgreSQL or MySQL are optional |
| Redis | Only [high availability](high-availability.md) |

## 3. Install on your computer

1. Get the host: download the `BaGet` workflow artifact of a release run, or publish it:
   `dotnet publish src/BaGet -c Release -o artifacts -p:GeneratePackageOnBuild=false`, then extract.
2. Start it: `dotnet BaGet.dll`.
3. Browse `http://localhost:6060/`.

- The port comes from `Kestrel:Endpoints:Http:Url` in `appsettings.json`; `--urls` and `ASPNETCORE_URLS` have no effect. Override with the environment variable `Kestrel__Endpoints__Http__Url`.
- Migrations run at startup (`RunMigrationsAtStartup`, on by default).
- On a server with no users, startup creates the administrator `Admin` / `Admin1234!`. Change the password at once ([Default administrator](configuration.md#default-administrator)).

## 4. Install with Docker

1. Create `baget.env` and a `baget-data` folder (sample env file: [Docker](installation/docker.md#configure-baget-extension)). Minimum:

```
ApiKey=<a secret value>
Kestrel__Endpoints__Http__Url=http://+:80
Storage__Type=FileSystem
Storage__Path=/var/baget/packages
Database__Type=Sqlite
Database__ConnectionString=Data Source=/var/baget/baget.db
Search__Type=Database
SettingsOverridesPath=/var/baget/appsettings.Overrides.json
```

2. Build the image from the repository root: `docker build -t baget .` (or pull the one the release workflow publishes).
3. Run it:

```
docker run --rm --name nuget-server -p 6060:80 --env-file baget.env -v "$(pwd)/baget-data:/var/baget" baget:latest
```

Keep `SettingsOverridesPath` and the log path inside the mounted volume, or a recreated container loses them. The data protection key ring (`DataProtection-Keys`) is created beside the overrides file, so with the setting above it is in the volume too; without it a recreated container makes stored remote passwords unreadable and signs everyone out ([Data protection keys](configuration.md#data-protection-keys)). If the container is reached through a proxy or published port, the client address is the Docker gateway: list the proxy in `Proxy:TrustedProxies` / `Proxy:TrustedNetworks`.

## 5. Install on IIS

1. Install the .NET 10 Hosting Bundle, restart IIS.
2. Publish the host (`dotnet publish` generates `web.config`) and copy it to the site folder.
3. Create an application pool: no managed code, integrated pipeline.
4. Create a site on that pool with your host name and port (the Kestrel endpoint is not used in-process).
5. Grant the pool identity write access to the folder (required: startup stops if the key folder cannot be written; [IIS proxy](installation/iis-proxy.md)): the data protection key ring is written to `DataProtection-Keys` beside the overrides file (set `DataProtection:KeyPath` for another folder), so a pool without a loaded user profile no longer loses its keys.

## 6. Choose a database, storage and search

Set these in `appsettings.json`, environment variables or (after install) the Settings page. Full list: [Configuration](configuration.md).

| Subsystem | Setting | Values |
|---|---|---|
| Database | `Database:Type`, `Database:ConnectionString` | `Sqlite` (default), `SqlServer`, `PostgreSql`, `MySql`, `AzureTable` |
| Storage | `Storage:Type` | `FileSystem` (default), `AzureBlobStorage`, `AwsS3`, `GoogleCloud`, `AliyunOss`, `Null` |
| Search | `Search:Type` | `Database` (default), `AzureSearch`, `Null` |

Cloud setup: [Azure](installation/azure.md), [AWS](installation/aws.md), [Google Cloud](installation/gcp.md), [Aliyun](installation/aliyun.md).
Docker registry repositories need a storage that can stream (not `Null`): [Package formats](formats.md#streaming-storage-b2).

## 7. First steps after installing

| Step | How |
|---|---|
| Change the `Admin` password | Sign in, then the profile page |
| Set `ApiKey` or create user keys | Global `ApiKey` setting, or `/account/apikeys` |
| Publish a package | `dotnet nuget push -s http://localhost:6060/v3/index.json -k <key> pkg.nupkg` |
| Add the feed to clients | Source `http://localhost:6060/v3/index.json`; symbols `http://localhost:6060/api/download/symbols` |
| Behind a reverse proxy / HTTPS | Terminate TLS at the proxy; the path base is honoured, forwarded headers only from trusted proxies (loopback by default: list yours in `Proxy:TrustedProxies` / `Proxy:TrustedNetworks`, see [Configuration](configuration.md#reverse-proxies-and-forwarded-headers)) |
| Import existing packages | [nuget.org](import/nugetorg.md), [NuGet.Server](import/nugetserver.md), [local feeds](import/local-feeds.md) |

Next: the [user manual](user-manual.md).

## 8. Upgrade

Replace the files (or image) and restart; pending migrations apply at startup. Back up the database and the package store first.
