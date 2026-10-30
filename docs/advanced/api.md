# Server API

BaGet Extension implements the [NuGet V3 API](https://learn.microsoft.com/nuget/api/overview); clients find
every resource through the service index at `/v3/index.json`. This page lists what it serves, and
the endpoints BaGet Extension adds of its own. Every NuGet route below also exists under `/r/{name}/` for a named [repository](../repositories.md) (`/r/{name}/v3/index.json`); a repository of another format answers `404` on a route of a different format.

## Service index resources

| Resource | URL | Notes |
| --- | --- | --- |
| `PackagePublish/2.0.0` | `/api/v2/package` | `PUT` push; `DELETE /{id}/{version}` unlists or hard deletes per [`PackageDeletionBehavior`](../configuration.md#enable-package-hard-deletions); `POST /{id}/{version}` relists |
| `SymbolPackagePublish/4.9.0` | `/api/v2/symbol` | portable PDBs that match the package's assemblies |
| `SearchQueryService` | `/v3/search` (also advertised as `/3.0.0-beta`, `/3.0.0-rc`) | results include `deprecation`, `vulnerabilities`, `owners`, `verified` |
| `SearchAutocompleteService` | `/v3/autocomplete` | also advertised as `/3.0.0-beta`, `/3.0.0-rc`; `take` is clamped to 0..1000 and `skip` to 0 or more on search and autocomplete |
| `RegistrationsBaseUrl` | `/v3/registration` | entries include `deprecation` and `vulnerabilities`; also advertised as `/3.0.0-beta`, `/3.0.0-rc` |
| `PackageBaseAddress/3.0.0` | `/v3/package` | |
| `PackageDetailsUriTemplate/5.1.0` | `/packages/{id}/{version}` | |
| `ReportAbuseUriTemplate/3.0.0-beta`, `-rc` | `/packages/{id}/{version}/report-abuse` | |
| `VulnerabilityInfo/6.7.0` | `/v3/vulnerabilities/index.json` | one page, `/v3/vulnerabilities/base.json`; entries set by hand plus what [scanning](../vulnerability-scanning.md) found about the package version itself |
| `Catalog/3.0.0` | `/v3/catalog/index.json` | not with Azure Table Storage (the `Catalog` resource is not advertised and its routes answer `404`) |
| `RepositorySignatures/5.0.0` | `/v3/repository-signatures/index.json` | only while [repository signing](../configuration.md#repository-signing) is configured |

The V2 (OData) feed is at `/api/v2`; see [NuGet V2 feed](../configuration.md#nuget-v2-feed).

## Authentication

Send the key in the `X-NuGet-ApiKey` header: the global `ApiKey`, or with accounts a per-user key.

| Request | Needs |
| --- | --- |
| `PUT /api/v2/package`, `PUT /api/v2/symbol` | a key allowed to push the package id |
| `DELETE`/`POST /api/v2/package/{id}/{version}`, deprecation and vulnerability writes | a key with the `package:unlist` scope that may manage the package |
| `GET /v3/package/{id}/{version}/validation` | a key that may push or manage the package |
| every other `GET` in this page | `Read` on the repository ([access control](../access-control.md)); none while the repository is open to everyone, which is the default |

Reads of a repository that is not open to everyone take `Authorization: Basic` (user name = the key's owner, password = the
API key) or the same `X-NuGet-ApiKey` header: `401` + `WWW-Authenticate: Basic realm="BaGet"` for an anonymous caller,
`404` for an authenticated one without `Read` (nothing says the repository or package exists).

Writes: `401` without a valid key, `403` for a key that may not act on the package (no `Deploy`/`Delete`/`Annotate`
permission in that repository for the package id, or not its owner). With `IsReadOnlyMode` on, push,
delete, relist and the metadata writes answer `401`. While the [maintenance gate](../configuration.md#read-only-maintenance-gate) is closed (a database copy),
any database write answers `503` with `Retry-After`.

## Other routes

| Route | Notes |
| --- | --- |
| `GET /v3/registration/{id}/index.json`, `/v3/registration/{id}/{version}.json` | registration index and leaf |
| `GET /v3/package/{id}/index.json`, `/v3/package/{id}/{version}/{id}.{version}.nupkg`, `.../{id}.nuspec` | versions, package, manifest |
| `GET /v3/package/{id}/{version}/readme`, `.../icon` | the package's readme and icon |
| `GET /v3/dependents` | packages that depend on a package; BaGet Extension's own, not in the NuGet protocol |
| `GET /api/download/symbols/{file}/{key}/{file2}`, `/api/download/symbols/{prefix}/{file}/{key}/{file2}` | symbol download (the symbol server URL) |
| `GET /v3/vulnerabilities/index.json`, `/v3/vulnerabilities/base.json` | vulnerability index and its only page |
| `GET /v3/catalog/index.json`, `/v3/catalog/page{n}.json`, `/v3/catalog/data/{commitId}.json` | catalog index, page, leaf |
| `GET /v3/repository-signatures/{fingerprint}.crt` | the repository signing certificate |
| `GET /api/v2`, `/api/v2/$metadata`, `/api/v2/Packages()`, `/api/v2/Packages(Id='..',Version='..')`, `/api/v2/FindPackagesById()`, `/api/v2/Search()` | V2 feed; `404` when `Feeds:EnableV2` is `false`, while push, delete and relist under `/api/v2/package` and `/api/v2/symbol` are unaffected |

## Other formats

| Format | Routes (under `/r/{name}/` unless noted) | Docs |
| --- | --- | --- |
| npm | `npm/{package}` (packument `GET`, publish `PUT`, `…/-rev/{rev}`), `npm/{package}/-/{file}` (tarball), `npm/-/package/{package}/dist-tags[/{tag}]`, `npm/-/v1/search`, `npm/-/ping`, `npm/-/whoami`, `npm/-/v1/login`, `npm/-/user/{userKey}` (`PUT` login), `npm/-/user/token/{token}` (`DELETE` logout), `npm/-/npm/v1/security/advisories/bulk`, `npm/-/npm/v1/security/audits/quick`; a scoped name is `@scope%2fname` or `@{scope}/{pkg}` | [npm registry](../npm-registry.md) |
| Maven | `maven/{**path}`: `GET`/`HEAD` read, `PUT` deploy, `DELETE` | [Maven repositories](../maven-repositories.md) |
| Docker (OCI Distribution Spec 1.1) | at the **server root**: `/v2/`, `/v2/token`, `/v2/{repo}/{image…}/blobs/…`, `manifests/…`, `tags/list`, `referrers/…`; `{repo}` is the BaGet repository | [Docker registry](../docker-registry.md) |

## Health

`GET /health/live` answers `200` while the process runs; `GET /health/ready` answers `200` or `503` with the name of each check (database, storage; the shared store is reported only). Both are anonymous, see [High availability](../high-availability.md).

## Administration

Repositories, the access model, users, operations, retention, scanning and replication are administered under `/api/v1` with an administrator's
`admin:api` key; see [Administration REST API](../admin-api.md) and its OpenAPI document `GET /api/v1/openapi.json`.

## Deprecation and vulnerabilities

These change a package version, so they are authorised like a delete: the global `ApiKey`, or with
[accounts](../configuration.md#accounts-package-owners-and-api-keys) an owner's key with the
`package:unlist` scope, sent in the `X-NuGet-ApiKey` header.

| Request | Body |
| --- | --- |
| `PUT /v3/package/{id}/{version}/deprecation` | `{"reasons": ["Legacy"], "message": "...", "alternatePackageId": "...", "alternateVersionRange": "[2.0.0, )"}` |
| `DELETE /v3/package/{id}/{version}/deprecation` | |
| `PUT /v3/package/{id}/{version}/vulnerabilities` | `[{"advisoryUrl": "https://...", "severity": 2, "versionRange": "[1.0.0, 1.2.0)"}]`; `[]` clears them |

* `reasons`: one or more of `Legacy`, `CriticalBugs`, `Other`.
* `severity`: `0` low, `1` moderate, `2` high, `3` critical.
* `alternateVersionRange` is kept only with `alternatePackageId`.
* An omitted `versionRange` means the version itself.

| Response | Meaning |
| --- | --- |
| `204` | changed |
| `400` | invalid body; the response says why |
| `401` / `403` | not authenticated (or read-only mode) / not allowed for this package |
| `404` | no such package version |
| `501` | the database cannot store this metadata (Azure Table Storage) |

Owners can do the same from the package's manage page.

## Validation status

With the [validation pipeline](../configuration.md#validate-packages-before-serving-them) on:

`GET /v3/package/{id}/{version}/validation`, with an owner's key or the global `ApiKey` (`401`, `403`, and `404` when the version has no validation record):

```json
{
  "submissionId": "...",
  "status": "Quarantined",
  "steps": [
    { "type": "PackageEntries", "state": "Succeeded", "message": null },
    { "type": "PackageSignature", "state": "Failed", "message": "The package must be signed." },
    { "type": "Index", "state": "Failed", "message": "Quarantined: the package failed validation." }
  ]
}
```

`status` is `Validating`, `Available` or `Quarantined`.

## Web pages

Pages marked "accounts" answer `404` while `Accounts:Enabled` is `false`; "sign-in" pages redirect to `/account/login` when anonymous.

| Page | |
| --- | --- |
| `/`, `/packages` | search (same page) |
| `/packages/{id}`, `/packages/{id}/{version}` | package details |
| `/upload` | browser upload (stage, then confirm or cancel, with a metadata preview) and push instructions; needs an API key, or a sign-in with accounts |
| `/stats` | totals, most downloaded packages, latest versions |
| `/terms`, `/privacy` | the operator's Markdown files from `Site:TermsOfUsePath` / `Site:PrivacyPolicyPath`; linked from the footer only when set |
| `/packages/{id}/{version}/report-abuse` | records a report for the package's owners |
| `/account/login`, `/account/logout`, `/account/register` (needs `Accounts:AllowRegistration`), `/account/loginwith2fa`, `/account/loginwithrecoverycode` | accounts |
| `/account/forgotpassword`, `/account/resetpassword`, `/account/confirmemail` | accounts; `404` unless email (SMTP) is configured |
| `/account/apikeys`, `/account/organizations`, `/account/packages`, `/account/settings`, `/account/twofactor`, `/account/reauthenticate` | accounts, sign-in |
| `/account/profile` | accounts, sign-in; redirects to `/profiles/{name}` |
| `/profiles/{name}` | a user's or organization's packages; accounts |
| `/packages/{id}/manage` | accounts, sign-in; owners: owners, unlist/relist, delete (per `PackageDeletionBehavior`), deprecation, vulnerabilities, symbols (list/delete), abuse reports; administrators: verified flag |
| `/scan-artifact?name=&version=` (also `/r/{repo}/scan-artifact`) | accounts, sign-in; may manage the package: *Scan now* of one version, see [vulnerability scanning](../vulnerability-scanning.md) |
| `/admin/scanning`, `/admin/scanning/{repository}`, `/admin/scanning/projects/{name}` | administrators: scanning overview, a repository's dashboard and policy, a project's policy |
| `/packages/{id}/validation` | accounts, sign-in, validation; push or manage: the validation status of the ID's submissions |
| `/packages/{id}/contact-owners` | accounts, sign-in; `404` unless email is configured |
| `/admin/users` | administrators: promote, demote, reset password, send reset link |
| `/admin/users/{name}/packages`, `/admin/users/{name}/delete` | administrators |
| `/admin/packages` | administrators: packages without owners |
| `/admin/repositories`, `/admin/repositories/{name}`, `/admin/repositories/{name}/delete` | administrators: list, create, edit and delete repositories, see [Repositories](../repositories.md) |
| `/admin/groups`, `/admin/groups/{name}`, `/admin/permissions`, `/admin/permissions/{name}`, `/admin/projects`, `/admin/projects/{name}`, `/admin/audit`, `/admin/access-unavailable` | administrators (projects and their targets also their project administrators): groups, permission targets, projects, access audit, see [Access control](../access-control.md#admin-pages) |
| `/account/access` | accounts, sign-in: your effective permissions per repository |
| `/npm-packages`, `/npm-upload`, `/maven-artifacts`, `/maven-upload`, `/docker-images` (and the package, artifact and image detail pages) | the browse and upload pages of the npm, Maven and Docker repositories, also at `/r/{name}/…`; a page of another format answers `404` |
| `/admin/formats`, `/admin/operations`, `/admin/validation`, `/admin/abuse`, `/admin/cluster` | administrators: format modules, background operations, validation and abuse queues, cluster nodes and health |
| `/admin/retention`, `/admin/retention/{key}`, `/admin/retention/holds` | administrators: retention policies, dry run, legal holds |
| `/admin/replication`, `/admin/replication/{key}` | administrators: replication configurations and the run log |
| `/admin/api` | administrators: the catalog of the [administration REST API](../admin-api.md) |

[UI coverage](../ui-coverage.md) maps every capability to its page.
| `/admin/settings` | administrators: edit settings (saved to `appsettings.Overrides.json`), see the [Settings page](../configuration.md#settings-page) and [moving to another database](../configuration.md#moving-to-another-database); `404` if the overrides store is unavailable |
| `/admin/settings?handler=CopyStatus` | administrators: JSON progress of the database copy (`phase`, `active`, `finished`, `succeeded`, `restartRequired`, table and row counters, `message`, `errors`) |

Admin pages answer `404` to everyone else, so their existence is not disclosed. Paths are lowercase.
