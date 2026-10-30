# Architecture

BaGet Extension is one ASP.NET Core (`net10.0`) host serving the NuGet V3 protocol, an optional V2 feed, a
symbol server, npm, Maven and Docker (OCI) registries, an administration REST API and an MVC (controllers and views) UI.
Storage, database and search are pluggable and chosen from configuration at runtime.

## Projects

| Project | Role |
|---|---|
| `BaGet` | Host and CLI entry point: `Program.cs` (configuration, services, pipeline, commands) |
| `BaGet.Core` | Domain: entities, `IContext`, `IPackageDatabase`, storage, search, indexing, remote/virtual repositories, format modules (`Formats`: NuGet, npm, Maven, Docker), access model, retention, scanning, replication, operations, high availability (`Ha`), telemetry, options, settings, maintenance gate, database copy |
| `BaGet.Web.V3` | V3, npm, Maven and OCI controllers (`Controllers/Protocols`), the `/api/v1` administration API (`Controllers/Api`), the `/api/ui` JSON twins of the screens (`Controllers/UI`), `BaGetEndpointBuilder`, `Routes`, `IUrlGenerator` (`Routing`); no view |
| `BaGet.Web.FrontEnd`, `BaGet.Identity.FrontEnd` | The web screens: view controllers (`Controllers/FrontEnd`) and views; `AddBaGetFrontEnd()` |
| `BaGet.Api.Base`, `BaGet.Frontend.Base` | Shared base of the APIs and screens (twin bases, `[UiApi]`, `[RepositoryPage]`); MVC base of the screens (view controller bases, layout, link helpers) |
| `BaGet.Web.V2` | Legacy V2 OData/Atom feed (`Feeds:EnableV2`) |
| `BaGet.Identity` | Accounts, API keys, ownership, administrators, access-model, retention, replication, scanning and other admin pages (`Accounts:Enabled`) |
| `BaGet.Catalog` | V3 catalog resource, writer and rebuild |
| `BaGet.Validation` | Validation queue and worker, validators, repository signing (`Validation:Enabled`) |
| `BaGet.Database.{Sqlite,SqlServer,PostgreSql,MySql}` | One EF Core `IContext` and migrations per provider |
| `BaGet.Azure`, `BaGet.Aws`, `BaGet.Gcp`, `BaGet.Aliyun` | Cloud storage (Azure Blob, S3, Google Cloud, Aliyun OSS), Azure Table database and Azure Search |
| `BaGet.Protocol` | Standalone NuGet V3 **client** library (shipped as a package; used for mirroring) |

Dependencies point inwards: every project references `BaGet.Core`; only the host references everything. The host also holds the telemetry export (`src/BaGet/Telemetry`) and the Redis cluster store (`src/BaGet/Ha`).

`BaGet.Identity` cannot reference `BaGet.Validation` or `BaGet.Catalog`; it reaches them through `BaGet.Core` interfaces.

## Startup — `src/BaGet/Program.cs`

`Program` holds `Main` and `BuildApplication`; each step is an extension method in `src/BaGet/Hosting/`.

| Step | What happens |
|---|---|
| `Main` | McMaster `CommandLineApplication`: `import downloads [-r repo]`, `catalog rebuild [-r repo]`, `account reset-password/promote/demote/approve` (`AddBaGetCommands`); the default command runs migrations (while `RunMigrationsAtStartup` is on), promotes initial administrators and runs the host. Unknown arguments pass through to the host. |
| `AddBaGetSettingsOverrides` | `BAGET_CONFIG_ROOT` base path; inserts `appsettings.Overrides.json` (written by the Settings page) after `appsettings*.json`, before environment variables and the command line. |
| `AddBaGetHost` | Options binding and validation (`AddBaGetOptions<T>`), web app, accounts, provider registration (`app.AddXyz()` per subsystem). |
| `AddBaGetFrontEnd` | Adds the screens to a host: `Frontend.Base`, `Identity.FrontEnd` and `Web.FrontEnd` as MVC application parts, Razor Pages, `RepositoryUrlHelperFactory`. A host that serves only the APIs omits it |
| `AddBaGetWebApplication` | Registers the web APIs, without the screens (`/packages` is also routed to the home action; every repository page also at `/r/{repo}/…`, see below; Razor Pages, for a host's own pages, are registered by `AddBaGetFrontEnd` and mapped only when registered; BaGet ships none), the catalog and validation services and `StagingSweepWorker`; `AddBaGetApplicationParts` adds every BaGet assembly with controllers or pages (`Web.V3`, `Web.V2`, `Catalog`, `Validation`, `Identity.Api`; the screens' assemblies are added by `AddBaGetFrontEnd`) as an MVC application part, so a host built without the Web SDK (e.g. a worker service calling `BaGet.Program.Main`) still serves them. A new such assembly must be added there. |
| `UseBaGetPipeline` | Middleware (below), then `BaGetEndpointBuilder.MapEndpoints`. |

Some configuration is read **while registering services** (e.g. `Accounts:Enabled`, `Email`, provider types),
so those settings need a restart; options read through `IOptionsSnapshot` apply live.

## Request pipeline

```
HostFiltering → (Dev: exception page, status code pages) → ForwardedHeaders → PathBase
→ StaticFiles → Routing → RepositoryResolution → CORS → Authentication → Authorization → OperationCancelled → Maintenance → endpoints
```

- `AllowedHosts` drives host filtering: a request with any other `Host` header gets 400.
- Endpoints are named constants in `Routes.cs`, mapped in `BaGetEndpointBuilder.MapEndpoints`; absolute URLs come only from `IUrlGenerator`.
- Every protocol route exists at `/` (the `default` repository) and under `/r/{repo}/`; `RepositoryResolutionMiddleware`
  selects the repository from the route (`404` if unknown) and `IUrlGenerator` keeps advertised URLs inside it.
- The repository pages (controller actions marked `[RepositoryPage]`) get an `r/{repo}/` twin of each route (`RepositoryActionRouteConvention`);
  `RepositoryUrlHelper` adds the request's `repo` to links to those actions (ASP.NET Core would drop it between different actions) (a screen is
  its controller and default GET action, `ScreenRef`). `IRepositoryAccess` decides which repositories the switcher, the
  upload picker and `RepositoryActionFilter` offer.
- `MaintenanceMiddleware` turns a `MaintenanceException` into `503` + `Retry-After`.

## Runtime provider selection

Each subsystem is an `IProvider<TService>` chain. `GetServiceFromProviders<T>` asks every registered provider
and takes the first non-null answer; each provider decides from configuration.

| Subsystem | Key | Implementations |
|---|---|---|
| Database (`IContext`) | `Database:Type` | Sqlite, SqlServer, PostgreSql, MySql, AzureTable |
| Storage (`IStorageService`) | `Storage:Type` | FileSystem, AzureBlobStorage, AwsS3, GoogleCloud, AliyunOss, Null |
| Search (`ISearchService`, `ISearchIndexer`) | `Search:Type` | Database, AzureSearch, Null |

New implementation: register a provider in `DependencyInjectionExtensions.Providers.cs` and an `app.AddXyz()`
in `HostServiceCollectionExtensions.AddBaGetProviders` (`src/BaGet/Hosting`).

## Endpoints

| Area | Paths |
|---|---|
| Service index | `v3/index.json` |
| npm registry | `r/{repo}/npm/…` (packuments, tarballs, dist-tags, search, login, audit) |
| Maven | `r/{repo}/maven/{**path}` (`GET`/`HEAD`/`PUT`/`DELETE`) |
| Docker (OCI) | `v2/`, `v2/token`, `v2/{repo}/{image…}/blobs\|manifests\|tags\|referrers/…` at the server root |
| Administration API | `api/v1/…`, `api/v1/openapi.json` ([Administration REST API](../admin-api.md)) |
| Health | `health/live`, `health/ready` |
| Publish | `api/v2/package` (PUT push, DELETE delete/unlist, POST relist), `api/v2/symbol` |
| Content | `v3/package/{id}/index.json`, `…/{version}/{id}.{version}.nupkg`, `….nuspec`, `…/readme`, `…/icon` |
| Metadata | `v3/registration/{id}/index.json`, `v3/registration/{id}/{version}.json` |
| Search | `v3/search`, `v3/autocomplete`, `v3/dependents` |
| Deprecation, vulnerabilities | `v3/package/{id}/{version}/deprecation`, `…/vulnerabilities`, `v3/vulnerabilities/*` |
| Catalog | `v3/catalog/index.json`, `v3/catalog/page{n}.json`, `v3/catalog/data/{commitId}.json` |
| Validation, signing | `v3/package/{id}/{version}/validation`, `v3/repository-signatures/*` |
| Symbols | `api/download/symbols/…` |
| V2 feed | `api/v2`, `api/v2/$metadata`, `Packages()`, `FindPackagesById()`, `Search()` |
| UI | `/`, `/packages`, `/packages/{id}/{version}`, `/upload`, `/stats`, `/account/*`, `/admin/*`, `/profiles/*` (full list in the [Server API](api.md)) |

## Push

```
PUT api/v2/package
 → IAuthenticationService.AuthenticatePrincipalAsync(api key) → BaGetPrincipal
 → IPackageAuthorizationService.AuthorizeAsync(principal, Push, id)
 → IPackageSubmissionService.SubmitAsync
     validation off → IPackageIndexingService.IndexAsync
     validation on  → stage + ValidationQueue → ValidationWorker → validators → IndexAsync (or reject)
```

`PackageIndexingService.IndexAsync`:

1. Read the `.nupkg`; reject unsafe entries; extract nuspec, readme and icon.
2. Existing version → `PackageAlreadyExists`, or replace it when `AllowPackageOverwrites` is on.
3. Save files: `packages/{id}/{version}/{id}.{version}.nupkg`, `{id}.nuspec`, `readme`, `icon` (lowercased). Repositories other than `default` store under `repos/{name}/` (e.g. `repos/{name}/packages/…`; symbols, catalog, validation and staging likewise).
4. Add the database row (`IPackageDatabase.AddAsync`).
5. `ISearchIndexer.IndexAsync`.
6. `PackageChangeNotifier` → every `IPackageChangeHandler` (e.g. `CatalogWriter`). Runs after the commit; a
   handler's failure is logged, never fails the push.

Browser uploads (`/upload`) use the same submission, with an extra staging step (`IPackageStagingService`)
between "stage" and "confirm"; `StagingSweepWorker` removes abandoned uploads.

## Download and mirroring

`PackageContentController` → `IPackageContentService` → `IPackageService` → `IPackageStorageService`.
`PackageService` indexes a missing version from the repository's upstream (`IUpstreamClient`, V3
through `BaGet.Protocol` or V2) before serving it; mirrored packages go straight to
`IPackageIndexingService`, never through validation. The `IUpstreamClient` of a request is a `RemoteUpstreamClient`:
`IRemoteUpstreamSource` resolves the upstream of the current repository (a remote repository's own settings; for
`default`, the `mirror` remote repository while `Mirror:Enabled`; none for local repositories), then the client applies
offline mode, the metadata and negative cache periods and stale-on-error, and records the upstream's health
(`RemoteRepositoryState`, in memory). A remote repository that does not store locally is a pass-through
(`IPassThroughUpstreamClient`, `IPackageService.OpenUncachedAsync`). See [Remote repositories](../remote-repositories.md).

Deletion follows `PackageDeletionBehavior`: `Unlist` (hide) or `HardDelete` (rows and files).
Administrators can always hard-delete from the UI.

## Formats, remote and virtual repositories

| Piece | Job |
|---|---|
| `IFormatModule` / `FormatRegistry` (`BaGet.Core/Formats`) | Describes a format (`NuGet`, `Npm`, `Maven`, `Docker`): route prefix, creatable kinds, storage folders, create fields, usage and purge. Per-request work is in scoped `IFormatService`s; see [Formats](../formats.md) |
| `INpmRegistry`, `IMavenRepository`, `IOciRegistry` | Forward per call by `IRepositoryContext.Current.Kind` to the local, remote or virtual implementation |
| Remote (`RepositoryKind.Remote`) | Upstream in `Repository.Settings`; `RemoteUpstreamClient`, `RemoteRepositoryState`; see [Remote repositories](../remote-repositories.md) |
| Virtual (`RepositoryKind.Virtual`) | Ordered members, one deploy repository; see [Virtual repositories](../virtual-repositories.md) |

## Access model

`BaGet.Core/Access`: groups, permission targets (repository and package globs), project roles and an audit log; `IAccessEvaluator` decides
(`Read`, `Annotate`, `Deploy`, `Delete`, `Manage`), `RepositoryReadFilter` / `RepositoryActionFilter` enforce Read. See [Access control](../access-control.md).

## Retention, scanning, replication, high availability

| Subsystem | Code | Docs |
|---|---|---|
| Retention | `BaGet.Core/Retention` (`RetentionEvaluator`, `RetentionEngine`, `RetentionWorker`) | [Retention](../retention.md) |
| Vulnerability scanning | `BaGet.Core/Scanning` (`IScanService`, `IScanGate`, OSV and Trivy, `ScanWorker`) | [Vulnerability scanning](../vulnerability-scanning.md) |
| Replication | `BaGet.Core/Replication` (`ReplicationEngine`, `IFormatReplicator`, `ReplicationWorker`) | [Replication](../replication.md) |
| High availability | `BaGet.Core/Ha` (`IClusterStore`, `ClusterJobLease`, `ClusterSyncService`), Redis in `src/BaGet/Ha` | [High availability](../high-availability.md) |

## Authentication and authorization

| Piece | Behaviour |
|---|---|
| `BaGetPrincipal` | Who a key or session is: superuser flag, administrator flag, scopes, package glob |
| Accounts off | `ApiKeyAuthenticationService`: the global `ApiKey` (or none) is the superuser |
| Accounts on | `AccountAuthenticationService`: global key = superuser, else a hashed per-user key; `SessionPrincipalFactory` for browser sessions |
| `OwnershipAuthorizationService` | Ownership rules (new ID claimed by pusher, owned IDs need an owner, ownerless pre-account IDs only superuser/admin); administrators skip ownership but keep their key's scopes and glob |

Identity uses EF stores on whichever `IContext` the database provider resolved; the security stamp is checked
on every request.

## Data

`AbstractContext<T> : IdentityDbContext<BaGetUser>, IContext` holds the model: packages, dependencies,
target frameworks, package types, deprecations, vulnerabilities, validations, catalog entries, abuse reports,
users, organizations, owners, API keys, repositories and projects. Each provider subclasses it and owns its
migrations; a schema change needs a migration in all four relational providers.

### Repositories

Every package, owner, catalog entry, validation, abuse report and symbol record (`PackageSymbol`: which package version a PDB came from) belongs to a `Repository` (`RepositoryKey`;
`IRepositoryScoped`). `default` (key 1) is the one served at `/` and holds everything that predates repositories:
the `AddRepositories` migration gives existing rows `RepositoryKey = 1`, and startup creates the `default` row
(`IRepositoryService.EnsureDefaultAsync`). Package IDs are unique per repository: `(RepositoryKey, Id, Version)`.

| Piece | Job |
|---|---|
| `IRepositoryContext` (scoped) | The repository in effect: `Current { Key, Name, Format, Kind }`, `Use(info)` to select. Defaults to `default`. Routing, workers and commands call `Use` (or `IServiceScopeFactory.CreateRepositoryScope(info)`) before any query |
| `AbstractContext` query filters | Filter the six tables above on `Current.Key` per query, and stamp new rows with it on save. `IgnoreQueryFilters()` sees every repository (database copy, account deletion, the validation worker's pending list) |
| `IRepositoryService` | Find / list / create repositories; name rules in one place, `RepositoryNames` (lowercase `[a-z0-9-._]`, 1-64, not a reserved route name) |
| Azure Table | Holds the `default` repository only: creating another is refused, `TablePackageDatabase` refuses any other |

Other repositories are local, [remote](../remote-repositories.md) (`Repository.Settings` holds the upstream; the password is protected by data protection) or [virtual](../virtual-repositories.md) (`Repository.Settings` holds the member names and the deploy repository; reads run in each member's own scope through `VirtualRepositoryReader`, writes are redirected to the deploy repository). Package and symbol storage paths nest
under `repos/{name}/` for other repositories.

## Background work

| Hosted service | Project | Job |
|---|---|---|
| `ValidationWorker` | `BaGet.Validation` | Runs queued validations, then indexes or rejects |
| `StagingSweepWorker` | `BaGet.Web.V3` | Deletes expired browser-upload staging |
| `RetentionWorker` | `BaGet.Core` | Starts due retention schedules, flushes download usage |
| `ScanWorker` | `BaGet.Core` | Scans queued artifacts and sweeps repositories (`Scanning:*`) |
| `ReplicationWorker` | `BaGet.Core` | Starts due and deploy-triggered replications |
| `ClusterSyncService` | `BaGet.Core` | Heartbeat and shared gate / access model / copy status (`Ha:Enabled`) |
| `DatabaseCopyWorker` | `BaGet.Core` | Runs the queued database copy |
| `BackgroundEmailService` | `BaGet.Identity` | Sends queued email (confirmation, reset, contact owners) |

## Second databases: `IDatabaseContextFactory`

Each relational provider (`Add{Sqlite,SqlServer,PostgreSql,MySql}Database`; none for AzureTable) registers an
`IDatabaseContextFactory` that opens an `IContext` on an arbitrary connection string, configured like the
application's own context. Resolve `IEnumerable<IDatabaseContextFactory>` and match `DatabaseType`; the caller
disposes the context. `IContext.BeginKeyedInsertAsync` / `EndKeyedInsertAsync` bracket inserts with explicit keys
(SQL Server `IDENTITY_INSERT`; PostgreSQL `setval` afterwards; nothing for SQLite and MySQL).

## Read-only gate

`MaintenanceGate` (`BaGet.Core`, singleton, in memory) closes every database write.
`MaintenanceSaveChangesInterceptor` throws `MaintenanceException` from `SaveChanges` while it is closed; the
middleware answers `503`; the layout shows a banner. `CloseUntilRestart` cannot be reopened. A context built with
`MaintenanceGate.AllowWrites` (the copy's target) is exempt. Code that catches broad `Exception` around a write
must not swallow `MaintenanceException`. Raw SQL would bypass the gate, so there is none outside migrations.

## Database copy

The Settings page's "Copy all data" option queues a `DatabaseCopyRequest` (`DatabaseCopyService.TryQueue` closes the
gate and refuses a second copy); `DatabaseCopyWorker` runs it one at a time. Tables are copied in foreign-key order
(`DatabaseTable.InDependencyOrder`, Identity's included) in batches of 1000 ordered by key; shadow properties are
captured with the row. Pre-flight (`DatabaseCopyPreflight`) runs before the target changes; verification compares
an order-independent key checksum; progress is in memory, served as JSON by `/admin/settings?handler=CopyStatus`.
After settings saving starts a failure keeps the target and closes the gate until restart. See
[Moving to another database](../configuration.md#moving-to-another-database).

## Settings

`AddBaGetOptions<T>` records each options type as a `SettingsSchemaRegistration`. `SettingsCatalog` reflects
over them (descriptions from XML docs, `[SensitiveSetting]`, `[RestartRequired]`); `SettingsService` validates
and writes `appsettings.Overrides.json` (path: `SettingsOverridesPath`) through `SettingsOverridesStore`;
administrators edit it on `/admin/settings`. `[AllowedSettingValues]` gives a string setting its choices and is also
what startup validation checks (`Database/Storage/Search:Type`).

## Tests

| Project | Covers |
|---|---|
| `BaGet.Core.Tests` | Domain services, settings, storage, indexing (unit) |
| `BaGet.Tests` | Integration: `BaGetApplication : WebApplicationFactory<Program>` — real pipeline over HTTP, SQLite in a temp directory, `SystemTime` fixed to 2020-01-01Z |
| `BaGet.Protocol.Tests`, `BaGet.Protocol.Samples.Tests` | Client library |
| `BaGet.Web.Tests`, `BaGet.Azure.Tests` | Web helpers, Azure providers |
| `BaGet.Storage.Tests` | Streaming storage contract per cloud provider on emulator containers; skipped without Docker |
| `BaGet.Clients.Tests` | Real `npm`, `mvn` and `docker` clients against the running server; skipped without Docker |
| `BaGet.Ha.Tests` | Redis cluster store on a container; skipped without Docker |
| `BaGet.Telemetry.Tests` | Telemetry export against Jaeger and an OpenTelemetry Collector containers (traces, metrics, logs, dead and stalled endpoints); skipped without Docker |
| `BaGet.Database.Tests` | Database copy between every pair of SQLite / SQL Server / PostgreSQL / MySQL on Testcontainers servers; skipped without Docker |

xUnit v3 on Microsoft.Testing.Platform; see [Debugging](debugging.md) for commands.
