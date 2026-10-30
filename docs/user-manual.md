# User manual

Task-oriented guide to BaGet Extension: install, sign in, publish, consume, administer. Each section links to the page with the details.

## 1. Install and start

| Where | Page |
|---|---|
| Your computer | [On your computer](installation/local.md) |
| Docker | [Docker](installation/docker.md) |
| Azure, AWS, Google Cloud, Alibaba Cloud | [Azure](installation/azure.md), [AWS](installation/aws.md), [Google Cloud](installation/gcp.md), [Aliyun](installation/aliyun.md) |
| Windows behind IIS | [IIS proxy](installation/iis-proxy.md) |

- Default address `http://localhost:6060` (`Kestrel__Endpoints__Http__Url` changes it; `--urls` has no effect).
- The first start applies database migrations.
- With no users, startup creates the administrator `Admin` / `Admin1234!`. Change the password at once ([Default administrator](configuration.md#default-administrator)).

## 2. Sign in and API keys

| Task | Where |
|---|---|
| Sign in, register, profile | The sign-in page (registration is `Accounts:AllowRegistration`; with `Accounts:RequireApproval` an administrator must approve new accounts on `/admin/users`) |
| Create an API key (scopes, expiry, repository limit) | `/account/apikeys` |
| Package owners, organizations | [Access control](access-control.md) |

A push needs an API key (`-k`): a user key, or the global `ApiKey` when one is set.

## 3. Publish and consume

Repositories have a format. `default` is at `/`; any other is at `/r/{name}/`. See [Repositories](repositories.md) and [Package formats](formats.md).

| Format | Publish | Consume | Details |
|---|---|---|---|
| NuGet | `dotnet nuget push -s http://localhost:6060/v3/index.json -k <key> pkg.nupkg` (symbols: `.snupkg`) | Source `http://localhost:6060/v3/index.json`; V2 clients `http://localhost:6060/api/v2` | [Local install](installation/local.md), [Server API](advanced/api.md) |
| npm | `npm publish` | `npm install` with the repository's `.npmrc` lines | [npm registry](npm-registry.md) |
| Maven | `mvn deploy` | `settings.xml` / POM snippets | [Maven repositories](maven-repositories.md) |
| Docker | `docker login`, `docker push` | `docker pull` | [Docker registry](docker-registry.md) |

Each package page shows the exact install commands for its repository. Browser upload: `/upload` (preview, then confirm).

## 4. Search, browse, inspect

- The front page and `/packages` list and search packages; a package page shows versions, dependencies, owners, downloads and vulnerability findings.
- The header switcher moves between repositories (shown when there are two or more).
- Owners manage a package (unlist, deprecate, owners, symbols) on its Manage page; a report goes through Report abuse.

## 5. Administer

Administrator pages live under `/admin` (others get `404`).

| Task | Page |
|---|---|
| Settings, live (saved to `appsettings.Overrides.json`), database copy | `/admin/settings`, [Configuration](configuration.md) |
| Repositories: create local, [remote](remote-repositories.md) and [virtual](virtual-repositories.md) | `/admin/repositories` |
| Users, groups, permission targets, projects, audit | `/admin/users`, `/admin/groups`, `/admin/permissions`, `/admin/projects`, `/admin/audit`; [Access control](access-control.md) |
| Background operations (downloads import, catalog rebuild, Docker GC) | `/admin/operations` |
| Validation and abuse queues | `/admin/validation`, `/admin/abuse` |
| Retention policies and holds | `/admin/retention`; [Retention](retention.md) |
| Vulnerability scanning | `/admin/scanning`; [Vulnerability scanning](vulnerability-scanning.md) |
| Replication to or from another server | `/admin/replication`; [Replication](replication.md) |
| Cluster nodes and health | `/admin/cluster`; [High availability](high-availability.md) |
| Scripting everything above | [Administration REST API](admin-api.md) (`/api/v1`, `admin:api` key) |

Command line (run in the host folder):

| Command | Does |
|---|---|
| `import downloads [-r <repo>]` | Imports nuget.org download counts |
| `catalog rebuild [-r <repo>]` | Appends missing catalog entries |
| `account reset-password <user> [--email <address>]` | Sets a generated password, switches off two-factor |
| `account promote <user>` / `account demote <user>` | Grants or removes the administrator flag (promoting also approves a pending account) |
| `account approve <user>` | Enables an account waiting for approval (`Accounts:RequireApproval`) |

## 6. Operate

| Topic | Notes |
|---|---|
| Read-only mode | While a database copy runs, writes answer `503` + `Retry-After`; sign-in still works |
| Health | `GET /health/live`, `GET /health/ready` |
| Logs | Serilog, `logs/baget-.log`; levels on the Settings page |
| Telemetry | OpenTelemetry export to an OTLP endpoint, set on the Settings page |
| Moving packages in | [nuget.org](import/nugetorg.md), [NuGet.Server](import/nugetserver.md), [local feeds](import/local-feeds.md) |
| Troubleshooting | [Debugging](advanced/debugging.md) |

Every capability and the page that offers it: [UI coverage](ui-coverage.md).
