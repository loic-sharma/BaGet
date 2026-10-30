# DevOps manual

Build, release, deploy, run and monitor BaGet Extension. Details are in the linked pages.

## 1. Build and test

| Task | Command |
|---|---|
| Build | `dotnet build BaGet.sln -p:GeneratePackageOnBuild=false` |
| Test | `dotnet test BaGet.sln -p:GeneratePackageOnBuild=false` |
| Publish the host | `dotnet publish src/BaGet -c Release -o artifacts -p:GeneratePackageOnBuild=false` |
| Local wrapper | `build.ps1` (clean, restore, build; `-Run`, `-Publish`) |

- Always pass `-p:GeneratePackageOnBuild=false`: otherwise a build packs into `Packages\`, a shared NuGet feed.
- Create the `Packages` folder on a clean checkout (`mkdir Packages`), or restore fails with `NU1301`.
- Needs the .NET 10 SDK. Tests are xUnit v3 on Microsoft.Testing.Platform.
- Projects `BaGet.Clients.Tests`, `BaGet.Storage.Tests`, `BaGet.Database.Tests`, `BaGet.Telemetry.Tests` and `BaGet.Ha.Tests` use Docker (Testcontainers) and skip without it.

## 2. CI and release (`.github/workflows`)

| Workflow | Trigger | Does |
|---|---|---|
| `main.yml` (Build) | push / pull request on `main`, manual | Build and test on Ubuntu |
| `docs.yml` (Docs) | push on `main` | `mkdocs build --strict`, deploys to GitHub Pages |
| `release.yml` (Release) | manual; inputs `releaseVersion`, `packageSource` | `verify` (tests), then in parallel: `BaGet` zip artifact, NuGet packages pushed with `--skip-duplicate`, Docker image to Docker Hub |

| Secret / variable | Used for |
|---|---|
| `NUGET_API_KEY` | Pushing packages to the package source |
| `DOCKER_USERNAME`, `DOCKER_PASSWORD` | Docker Hub login |
| `DOCKER_IMAGE` (variable) | Image name; default `<DOCKER_USERNAME>/baget` |

Pages must use the "GitHub Actions" source for the docs site.

## 3. Deploy

| Target | Page |
|---|---|
| Docker (image from `Dockerfile`: `aspnet:10.0`, port 80, `dotnet BaGet.dll`) | [Docker](installation/docker.md) |
| IIS | [IIS proxy](installation/iis-proxy.md) |
| Azure, AWS, Google Cloud, Alibaba Cloud | [Azure](installation/azure.md), [AWS](installation/aws.md), [Google Cloud](installation/gcp.md), [Aliyun](installation/aliyun.md) |
| Several nodes | [High availability](high-availability.md) |

Configuration is layered: `appsettings.json`, `appsettings.Overrides.json` (written by the Settings page; path `SettingsOverridesPath`), environment variables (`Section__Key`), command line. Keep `SettingsOverridesPath` and log files on a persistent volume. Full reference: [Configuration](configuration.md).

## 4. Run

- Listens on `Kestrel:Endpoints:Http:Url` (`http://localhost:6060` by default; override with `Kestrel__Endpoints__Http__Url`). `--urls` has no effect.
- Pending migrations apply at startup (`RunMigrationsAtStartup`).
- Behind a proxy, TLS ends at the proxy; the path base is honoured and forwarded headers only from trusted proxies (loopback by default; list yours in `Proxy:*`, see [Configuration](configuration.md#reverse-proxies-and-forwarded-headers)).
- The key ring lives in `DataProtection-Keys` beside the overrides file: keep it on the persistent volume.
- Restrict served host names, and refuse private upstream addresses for remote repositories: [Configuration](configuration.md).

## 5. Monitor

| Signal | How |
|---|---|
| Liveness | `GET /health/live` (200 while the process answers) |
| Readiness | `GET /health/ready` (200 or 503 by database and storage; Redis only reported); point the load balancer here |
| Logs | Serilog: console, `logs/baget-.log` (daily, 14 kept), Windows event log (Warning+); levels live on the Settings page. No query strings or headers are logged |
| Traces, metrics, logs | OpenTelemetry export to an OTLP endpoint (`Telemetry:*`), live on the Settings page, with a status table and a connection test: [Telemetry](configuration.md#telemetry) |
| Cluster | `/admin/cluster`: nodes, leases, gate, health |
| Background jobs | `/admin/operations`, `/admin/retention`, `/admin/replication` |

## 6. Operate

| Task | How |
|---|---|
| Read-only during maintenance | In-memory gate; writes answer `503` + `Retry-After: 60`, reads work: [Configuration](configuration.md#read-only-maintenance-gate) |
| Move to another database | Settings page, "Copy all data": verified copy, gate closed until restart: [Moving to another database](configuration.md#moving-to-another-database) |
| Clean up old versions | [Retention](retention.md), with dry run and holds |
| Mirror to another server | [Replication](replication.md) |
| Scan for vulnerabilities | [Vulnerability scanning](vulnerability-scanning.md) |
| Automate administration | [Administration REST API](admin-api.md) with an `admin:api` key |
| Account recovery | `dotnet BaGet.dll account reset-password <user>`, `account promote|demote|approve <user>` |

## 7. Back up and upgrade

- Back up the database, the package store (file system folder or bucket), `appsettings.Overrides.json` and, in a cluster, the certificate in `Ha:KeyCertificatePath`.
- Upgrade: back up, deploy the new build or image, restart; migrations apply at startup. In a cluster, roll nodes one at a time behind `/health/ready`.
- A database copy is refused while `Ha:Enabled` is on (see [High availability](high-availability.md)).
- **Upgrade note (several OpenID Connect providers).** `Accounts:OpenIdConnectProviders:<id>` adds named providers beside the single `Accounts:OpenIdConnect` block, which keeps working unchanged as provider `oidc` (callback `/signin-oidc`, linked logins untouched, no migration). Register `/signin-oidc-<id>` as the redirect URI at each new provider. `/api/ui/session/external/oidc` became `/api/ui/session/external/{provider}`; `GET /api/ui/session/providers` lists the providers anonymously (the sign-in screen model also gains `externalProviders`) (`hasExternalProvider` / `externalProviderName` stay), the sign-in form posts `provider`, and a re-authentication uses a provider linked to the account. Public constructors and methods keep their old signatures as overloads. A provider with an administrator claim controls the administrator flag of the users who sign in through it ([configuration](configuration.md#several-providers)).
- **Upgrade note (Identity tables renamed).** Migration `RenameAspNetTables` renames `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens` and `AspNetRoleClaims` to `Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens` and `RoleClaims` (keys, indexes and foreign keys follow). Back up first; external SQL, reports or views that name the old tables must be updated, and during a rolling upgrade of a cluster old nodes cannot run against the renamed tables. On SQLite the foreign keys are rebuilt outside a transaction (EF warns at startup): an interrupted migration needs the backup.
- **Upgrade note (page models removed).** The `[Obsolete]` Razor Page models of `BaGet.Identity` and `BaGet.Web.V3` (`LoginModel`, `UsersModel`, `IndexModel`, `AccountPageModel`, ...), `RepositoryPageFilter` and `RepositoryPageRouteConvention` no longer exist: code of a host that referenced them must move to the controllers, workflows and `RepositoryActionFilter`. The forwarding views at the old `Pages/...` paths (`Pages/Shared/_Layout.cshtml`, `Pages/Admin/_ConfirmPassword.cshtml`, ...) are gone too: a host view must name the partial (`<partial name="_ConfirmPassword">`) or use `~/Views/Shared/...`. URLs, forms and `?handler=` values are unchanged (page names stopped resolving later, see the next note).
- **Upgrade note (`/api/ui` controllers renamed; breaking for code that references them).** Every controller named `…UiApiController` is now `…ApiController` (`AccountUiApiController` → `AccountApiController`, `SessionUiApiController` → `SessionApiController`, …), and so are the bases: `UiApiControllerBase` → `ApiControllerBase`, `AccountUiApiControllerBase` → `AccountApiControllerBase`, `AdminUiApiControllerBase` → `AdminApiControllerBase`, `AccessUiApiControllerBase` → `AccessApiControllerBase` (not to be confused with `AdminApiController` / `AccessApiController`, the `/api/v1` bases). Routes, request and response bodies are unchanged; the OpenAPI `ui` document's operation tags follow the names (`AccountUiApi` → `AccountApi`). Recompile code that derives from or names these types; nothing is forwarded.
- **Upgrade note (screens named by controller and action; breaking).** `LegacyPageAttribute` is gone: a screen is identified by its controller and the action that answers a plain GET (`ScreenRef`, listed in `ScreenCatalog`; `ScreenEndpoints` tells screen actions from other endpoints). `Url.Page`, `asp-page` and `RedirectToPage` with a BaGet page name (`/Admin/Users`, `/Package`, ...) no longer resolve: a host that linked to a BaGet screen uses `Url.Action`/`asp-controller` + `asp-action`/`RedirectToAction` (`AdminUsers`/`Index`; `repo` is still added for repository pages) and for a handler `asp-route-handler`. Signatures changed: `ScreenNext.Screen` and `ScreenLink.Screen` are a `ScreenRef`, `IScreenLinks.AbsoluteLink(ScreenRef, values)`, `[UiApi(typeof(XController), nameof(XController.Index))]`, `[ApiOperation(summary, typeof(XController), nameof(XController.Index))]` (`UiPage` became `UiController`/`UiAction`/`UiScreen`), `FormatLink` is built with `FormatLink.ToScreen(id, label, controller, action)` or `FormatLink.ToPath(id, label, path)` (its constructor is private; `Page` is gone), `RepositoryEditViewModel.BrowseScreen`, `AccountControllerBase.AbsoluteLink` (was `PageLink`), `NavLinkTagHelper` reads `asp-controller`/`asp-action`. In the `/api/ui` JSON, `next.screen` is `{ "controller", "action" }` instead of a page name, and the OpenAPI extension `x-baget-ui-page` is `x-baget-ui-screen` (`Controller/Action`). URLs, forms and `?handler=` values are unchanged.
- **Upgrade note (assemblies split, UI API).** The screens' logic moved out of `BaGet.Identity` and `BaGet.Web.V3` into two new packages, `Extension.BaGet.Identity.Application` and `Extension.BaGet.Web.Application` (use cases, DTOs, the account services such as `ApiKeyService` and `PackageOwnershipService`, snippets, `ReadmeMarkdown`); `RequestPrincipalResolver` moved into `BaGet.Identity`. Every public type keeps its name and namespace and the old assembly forwards it (`[TypeForwardedTo]`), so a host that recompiles needs no change; deploy the new assemblies beside the old (`Extension.BaGet` brings them transitively). The internal `*Workflow` classes are gone. Every screen now has a JSON twin under `/api/ui` ([UI API](ui-api.md)) documented at `/scalar`; URLs, forms and `?handler=` values are unchanged and nothing needs configuring.
- **Upgrade note (namespaces follow folders).** In `BaGet.Catalog`, `BaGet.Identity`, `BaGet.Identity.Application`, `BaGet.Validation`, `BaGet.Web.Application` and `BaGet.Web.V3` every type sits in the namespace `RootNamespace + folder` (e.g. `BaGet.Web.V3.Routing.Routes`, `BaGet.Identity.Application.Models.Account.LoginViewModel`), one type per file, nested types made top-level (`PackagesModel.OwnedPackage` is `OwnedPackage`); the `[TypeForwardedTo]` entries of the old namespaces are gone. A host that referenced these types recompiles with new `using` lines; URLs, forms and settings are unchanged.
- **Upgrade note (web APIs split from the screens; breaking).** New packages `Extension.BaGet.Api.Base`, `Extension.BaGet.Identity.FrontEnd` and `Extension.BaGet.Web.FrontEnd`. `BaGet.Identity.Api` and `BaGet.Web.V3` now hold APIs only (`/api/ui` twins, `/api/v1`, protocols) and no longer reference `BaGet.Frontend.Base`; the view controllers (`Controllers/FrontEnd`) and all views moved to the two FrontEnd packages (namespaces unchanged), the layout and shared partials, `NavLinkTagHelper`, `RepositoryUrlHelper(Factory)` and `UiActionMap` to `BaGet.Frontend.Base` (namespaces `BaGet.Frontend.Routing` / `BaGet.Frontend.TagHelpers`), the twin bases, `[UiApi]`, `[RepositoryPage]`, `ScreenEndpoints` and `RequestPrincipalResolver` to `BaGet.Api.Base`. The shared MVC types were renamed from `BaGet.Identity.*` to `BaGet.Frontend.*` in the same release (`BaGet.Identity.Controllers.Base`, `.Mvc.*`, `.Security` become `BaGet.Frontend.Controllers.Base`, `.Mvc.*`, `.Security`); recompile against the new names, nothing is forwarded. A host that serves the web UI must reference `Extension.BaGet.Web.FrontEnd` and call `services.AddBaGetFrontEnd()` after `AddBaGetWebApplication` (it registers the screens as application parts); without the call it serves the APIs only (no screens, no Razor Pages, and a `/api/ui` answer that links to a screen cannot build that link). `[UiApi]` and `[ApiOperation]` take the screen as strings (`[UiApi("AdminUsers", "Index")]`, controller without the `Controller` suffix) instead of `typeof(XController), nameof(XController.Index)`; `UiApiAttribute.Controller` and `ApiOperationAttribute.UiController` are strings. The protocol controllers derive `ControllerBase` instead of `Controller`. `UiControllerBase` implements the new marker `IScreenController`, which `ScreenEndpoints` tests.
- **Upgrade note (`/api/ui` accepts interactive sessions; behaviour change).** For single-page applications `/api/ui` now also accepts a person's interactive session: a **session token** from `POST /api/ui/session` (any origin; `bgs.` tokens, only `/api/ui` accepts them) and, same-origin, the **cookie together with a valid `X-XSRF-TOKEN` header** from `GET /api/ui/antiforgery` (the bare cookie is still ignored; the endpoint exists again). The account self-service and package ownership twins, which answered `403` to everybody over HTTP, now answer an interactive session normally and keep answering `403` to API keys and anonymous callers; the administration twins also admit an administrator's session (non-administrators get `403`); the global `ApiKey` is still refused there. New settings: `Accounts:SessionTokenLifetime`, `Accounts:SessionRefreshLifetime`, `Accounts:SessionMaxLifetime`, `Cors:AllowedOrigins` (an explicit list gives those origins credentialed access to `/api/ui` only; with no list the sign-in endpoints of `/api/ui` (`session*`, `antiforgery`, `account/*`) are same-origin only, so a separate-origin SPA must list its origin), `Frontend:SpaPath`, `Frontend:SpaBasePath`, `Frontend:Mvc`, `Frontend:SpaLinks`, `Frontend:SpaUrl` ([configuration](configuration.md#single-page-applications)). A host that calls `AddBaGetAccounts` gets the antiforgery header name `X-XSRF-TOKEN` configured (the HTML forms are unaffected). Changing a password or two-factor settings ends the account's other token sessions. A host that serves the screens itself and wants none of this changes nothing.
- **Upgrade note (`/api/ui` is keys only; superseded by the note above).** `/api/ui` no longer accepts the browser cookie or HTTP Basic (use `Authorization: Bearer <key>` or `X-NuGet-ApiKey`, as for `/api/v1`); `GET /api/ui/antiforgery` and the antiforgery requirement are gone; account self-service and package ownership twins answer `403` over HTTP (use the web screens); the global `ApiKey` is refused on the administration twins (use an administrator's `admin:api` key). The web screens are unchanged.
- **Upgrade note (forwarded headers, key ring, form limit, exit codes; read before upgrading behind a proxy or in a container).**
  - **Forwarded headers are trusted from loopback only.** `X-Forwarded-For`/`-Proto` used to be believed from any sender. A proxy that is not on the same machine must now be listed in `Proxy:TrustedProxies` / `Proxy:TrustedNetworks` (for example the Docker bridge network `172.17.0.0/16`, or your ingress or load balancer addresses); otherwise the client address is the proxy's and an `https` front end is seen as `http` (advertised URLs, `Secure` cookies). `Proxy:TrustAnyProxy=true` restores the old behaviour; use it only when clients cannot reach BaGet except through the proxy. See [Configuration](configuration.md#reverse-proxies-and-forwarded-headers).
  - **The key ring is persisted by every single node**, in `DataProtection-Keys` beside the overrides file (`DataProtection:KeyPath`). Keys kept so far in the user profile (`~/.aspnet/DataProtection-Keys`, `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`) are not read: after the upgrade stored remote-repository passwords and replication peer keys that were protected with them cannot be decrypted (re-enter them), and everyone is signed out once. To keep them, copy that folder to the new `KeyPath` before the first start. Put the folder on the persistent volume and make it writable (startup stops otherwise); on Windows the keys are DPAPI-encrypted, elsewhere plain XML, so restrict the folder's permissions. See [Data protection keys](configuration.md#data-protection-keys).
  - **Forms accept 1 MiB** (multipart); the push and upload endpoints keep the old ceiling.
  - **`baget` returns an exit code**: `1` for an invalid configuration or a refused command (it was always `0` for the first).
- **Upgrade note (ownership foreign keys).** The migration `AddOwnershipForeignKeys` adds foreign keys from repository, project, user and replication-configuration references to their rows. Before it does, it **deletes the rows that already pointed at nothing** (for example packages or API keys of a repository or user that no longer exists, and everything under them) and clears references to missing projects; such rows were invisible to the application. Back up first if you want to inspect them. From then on the database refuses an orphan, and deleting a repository, project or user is done by the application, which removes the dependents first.
  - **Rolling upgrade (cluster):** until every node runs the new build, do not delete an image, a project or a repository: the old code relies on cascades and cleanup the foreign keys now forbid (the delete fails with a constraint error, nothing is lost).
  - **Large databases:** the cleanup and the foreign-key validation scan whole tables. Migrations run with the provider's command timeout (30 s by default on SQL Server, PostgreSQL and MySQL); raise it in the connection string (`Command Timeout=600` for SQL Server and PostgreSQL, `Default Command Timeout=600` for MySQL) for the upgrade start, or apply the migrations with `dotnet ef database update` from a shell with such a connection string.
  - **MySQL DDL is not transactional:** if the migration fails half way, the foreign keys already added stay. Fix the cause, then drop the `FK_*_Repositories_RepositoryKey`, `FK_*_Projects_ProjectKey`, `FK_*_AspNetUsers_UserId`, `FK_ReplicationRuns_*`, `FK_ReplicationItems_*` and `FK_OciTags_OciRepositories_ImageKey` constraints that exist and start BaGet again (or restore the backup); the cleanup is repeatable. SQL Server, PostgreSQL and SQLite roll the whole migration back.
  - A database whose `default` repository row is missing (migrated by an early build of `AddRepositories` and never started) gets the row created first; rows of repository 1 are never treated as orphans.

## 8. High availability checklist

Shared SQL Server, PostgreSQL or MySQL; shared object storage (not the file system); Redis; `Ha:Enabled=true` with `Ha:ConnectionString`; the same `Ha:KeyPrefix` on every node; `Ha:KeyCertificatePath` set so data protection keys are encrypted. A node that breaks a rule does not start ([High availability](high-availability.md)).
