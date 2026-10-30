# How-to

Short recipes for common tasks. `host` is your server (default `http://localhost:6060`); `{name}` is a repository name. Details are in the linked pages.

## Publish and restore

| Goal | Steps |
|---|---|
| Push a NuGet package | Create a key on `/account/apikeys`, then `dotnet nuget push -s http://host/v3/index.json -k <key> pkg.nupkg` |
| Push symbols | Same command with a `.snupkg`; symbol location `http://host/api/download/symbols` |
| Use the server as a source | Add `http://host/v3/index.json` to `NuGet.config` or Visual Studio |
| Push from the browser | `/upload`: stage, preview the metadata, confirm |
| Use V2 clients | Source `http://host/api/v2` (`Feeds:EnableV2`, on by default) |

## Create a second repository

1. Sign in as an administrator, open `/admin/repositories`.
2. Create a local repository, pick its format (NuGet, npm, Maven, Docker) and a name.
3. Its feed is `http://host/r/{name}/v3/index.json` (Docker: `host/v2/{name}/{image}`).

See [Repositories](repositories.md).

## Make a feed private

1. Open `/admin/repositories/{name}` and switch off *Anonymous read* (or edit the permission targets).
2. Give each reader a user API key.
3. Add the source with credentials:
   `dotnet nuget add source http://host/r/{name}/v3/index.json -n {name} -u <user> -p <key> --store-password-in-clear-text --allow-insecure-connections` (the last flag only for plain `http`).

See [Access control](access-control.md).

## Cache nuget.org (or another upstream)

1. `/admin/repositories`: create a [remote repository](remote-repositories.md) with the upstream URL (add credentials if needed).
2. Point clients at the new repository's feed. Packages are stored on first download; offline mode and purge are on its edit page.
3. To combine local and cached packages under one URL, create a [virtual repository](virtual-repositories.md) with both as members and pick the deploy repository for uploads.

## Use npm

```
npm config set registry http://host/r/{name}/npm/
npm login
npm publish
```

Or a scope only: `@acme:registry=http://host/r/{name}/npm/` in `.npmrc`; auth via `//host/r/{name}/npm/:_authToken=<API key>`. See [npm registry](npm-registry.md).

## Use Maven

Create a Maven repository, put its API key in `settings.xml` and the repository in the POM; the package page generates both snippets. `mvn deploy` publishes. See [Maven repositories](maven-repositories.md).

## Use Docker

```
docker login host                      # user name + API key as password
docker push host/{name}/myimage:1.0
docker pull host/{name}/myimage:1.0
```

Plain `http` works only for `localhost` or hosts in `insecure-registries`. See [Docker registry](docker-registry.md).

## Clean up old versions

1. `/admin/retention`: new policy for a repository or project (keep the newest N, older than days, unused, cache size, untagged Docker manifests).
2. Run a dry run, read the plan, then save and run (the password is asked).
3. Protect versions with a hold; set a schedule for automatic runs.

See [Retention](retention.md).

## Scan for vulnerabilities

Set `Scanning:*` (off by default), then choose a policy per repository or project: warn, or refuse downloads and deploys. Findings show on package pages. See [Vulnerability scanning](vulnerability-scanning.md).

## Import existing packages

[From nuget.org](import/nugetorg.md), [from NuGet.Server](import/nugetserver.md), [from a local feed](import/local-feeds.md). Download counts: `dotnet BaGet.dll import downloads`.

## Regain admin access

| Case | Command |
|---|---|
| Forgot the password | `dotnet BaGet.dll account reset-password <user> [--email <address>]` |
| Need an administrator | `dotnet BaGet.dll account promote <user>` |

Both need `Accounts:Enabled`.

## Change a setting without editing files

`/admin/settings` (administrators): changes apply live unless marked restart-required, and are saved to `appsettings.Overrides.json`. See [Configuration](configuration.md#settings-page).

## Move to another database

Settings page: change `Database:Type` / `Database:ConnectionString`, keep *Copy all data* ticked, save. The server is read-only during the copy and stays so until you restart it. See [Moving to another database](configuration.md#moving-to-another-database).

## Script administration

Create an administrator key with scope `admin:api` and call `/api/v1` (OpenAPI at `/api/v1/openapi.json`, catalog at `/admin/api`). See [Administration REST API](admin-api.md).

## Run several nodes

Needs a shared database (not SQLite), shared object storage, Redis and `Ha:Enabled=true`; probe `/health/ready`. See [High availability](high-availability.md).

## See traces

Run Jaeger and set `Telemetry__Otlp__Endpoint=http://jaeger:4317`; see the Docker page's Jaeger example: [Docker](installation/docker.md#tracing-with-jaeger).
