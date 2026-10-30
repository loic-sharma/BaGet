# UI coverage

Every operation the server offers (REST, CLI, service) is reachable from the web UI, with the gate of the operation.
This page maps each capability to its page. Pages in repository scope also exist as `/r/{name}/...`. "Admin" = an
administrator signed in (others get 404). "Password" = the administrator's password (or a fresh external sign-in) is
asked again. Every write calls `MaintenanceGate.ThrowIfClosed` (503 while closed) and, on package pages, refuses in
`IsReadOnlyMode`. Tests: `UiCoverageTests` and the page test of each area; `UiCoverageInventoryTests` fails when a controller, screen (`Controller/Action` of a view controller) or CLI command has no row here, and `AdminApiAccessTests` when a REST operation has none. Every screen also has a JSON twin under `/api/ui` ([UI API](ui-api.md)); the twin is covered by its screen's row (no row of its own) and `UiApiParityTests` fails for a screen without one.

## NuGet protocol (per repository)

| Capability | Reached by | UI | Gate |
|---|---|---|---|
| Service index `v3/index.json` | REST | Admin Repositories / RepositoryEdit (feed URL + copy); Upload page (feed URLs) | read |
| Search, autocomplete | REST | Home and `/` search (`Home/Index`; filters, sort, paging); type-ahead on both search boxes | read |
| Package page, versions, dependents, readme, icon, download | REST | `/packages/{id}/{version}` (`Package/Show`), download link | read |
| Registration, flat container, `.nuspec` | REST | machine-only; the package page links the downloads | read |
| Push `.nupkg` | REST (`PUT api/v2/package`) | `/upload` (`Upload/Index`; stage, review, publish) | Push |
| Push `.snupkg` / symbols | REST (`api/v2/symbol`) | `/upload` (symbol package) | Push |
| Symbol download | REST | machine-only (debugger); symbols listed and deleted on Manage | read |
| Unlist / relist / delete | REST (`DELETE`, `POST`) | Manage (`PackageManage/Index`): Unlist, Relist, Delete; permanent delete asks Password | Manage (owner) |
| Deprecation (reasons, message, alternate ID and version range) | REST | Manage: Deprecate / Remove | Manage |
| Vulnerabilities | REST | Manage: add / remove | Manage |
| V2 feed | REST (`api/v2`) | Repositories: "V2 feed" link | read |
| Vulnerability index | REST | Repositories: "Vulnerabilities" link | read |
| Catalog index, pages, leaves | REST | Repositories: "Catalog" link (local repositories) | read |
| Catalog rebuild | CLI `catalog rebuild -r` | Admin Operations | Admin, audited |
| Repository signatures, certificate | REST | Repositories: "Signatures" link (local repositories) | read |
| Validation status of a submission | REST (`v3/package/{id}/{v}/validation`) | `/packages/{id}/validation` (`ValidationStatusPage/Index`), linked from Manage | Push or Manage |
| Import downloads | CLI `import downloads -r` | Admin Operations | Admin, audited |
| Report abuse | page | `/packages/{id}/{v}/report-abuse` (`ReportAbuse/Show`) | read |

## npm registry (per repository, `npm/`)

| Capability | Reached by | UI | Gate |
|---|---|---|---|
| Search `-/v1/search` | REST | `/npm-packages` (`NpmBrowse/Packages`) | read |
| Packument, tarball | REST | `/npm-packages/{name}` (`NpmBrowse/Package`; versions, tags, downloads) | read |
| Publish | REST (`PUT {name}`) | `/npm-upload` (`NpmBrowse/Upload`; file, tag) | Push |
| Dist-tags set / remove | REST | NpmManage (`NpmManage/Index`) | Manage |
| Deprecate / undeprecate a version | REST | NpmManage | Manage |
| Unpublish a version / all | REST (`DELETE`) | NpmManage | Manage + Password |
| Owners | REST | NpmManage | Manage |
| Login / logout (tokens) | REST (`npm login`, `-/user/token`) | Account API keys (create, list, revoke; npm tokens are keys limited to a repository) | signed in |
| Ping, whoami, audit | REST | stateless handshakes; whoami is the signed-in profile, audit answers "none" | none |
| Web login `-/v1/login` | REST | not offered (answers 404, npm falls back to user and password) | - |

## Maven repository (per repository, `maven/`)

| Capability | Reached by | UI | Gate |
|---|---|---|---|
| Read file, checksum, `maven-metadata.xml` | REST (`GET`/`HEAD`) | `/maven-artifacts` (`MavenBrowse/Artifacts`; browse, search), `/maven-artifacts/{group}/{artifact}/{version}` (`MavenBrowse/Artifact`; files with sizes and checksums, POM view, download links) | read |
| Deploy (`mvn deploy`, `PUT`) | REST | `/maven-upload` (`MavenBrowse/Upload`; coordinates, file, optional POM), `settings.xml` and `distributionManagement` snippets on browse and upload | Deploy |
| Delete a file | REST (`DELETE`) | MavenManage (`MavenManage/Index`) | Manage + Password |
| Delete a version / the artifact | service | MavenManage | Manage + Password |
| Snapshot clean-up (keep newest N builds) | service | MavenManage | Manage + Password |
| Owners | service | MavenManage | Manage |
| Policy, redeploy, snapshot builds kept | service | RepositoryEdit: Maven settings (audited) | Admin |
| Remote Maven repository: create (URL, credentials, periods, offline, store locally) | service | Repositories > *New remote repository* (format Maven) | Admin + Password |
| Remote Maven: edit upstream, *Test connection*, cache table, *Purge cache*, *Purge package* (`group:artifact`, version text) | service | RepositoryEdit (`/admin/repositories/{name}`) | Admin + Password |
| Remote Maven: read through (`GET`/`HEAD` of files, checksums, metadata) | REST | `/maven-artifacts` (the stored artifacts), snippets incl. the Central `<mirror>` | read |
| Virtual Maven repository: create, members, order, deploy repository, *Where would an artifact come from?* | service | Repositories > *New virtual repository* (format Maven); RepositoryEdit members and preview (`group:artifact`, any version) | Admin |
| Dependency / `settings.xml` / `<repositories>` / Gradle snippets | machine-only config | artifact and browse pages | read |

## Docker registry (per repository, `/v2/{repo}/`)

| Capability | Reached by | UI | Gate |
|---|---|---|---|
| Version check `GET /v2/` | REST | machine-only handshake (the browse page shows the registry address and `docker login`) | none (401 challenge) |
| Token service `GET /v2/token` (`docker login`) | REST | machine-only; Account API keys creates the key used as the password; the browse page shows the login, `curl` and daemon snippets | API key / anonymous |
| Pull: blobs (ranges), manifests, tag list, referrers (`?artifactType=` filter: the *Referrers* form on `?digest=`) | REST | `/docker-images` (`DockerBrowse/Images`; browse, search), `/docker-images/{image}` (`DockerBrowse/Image`; tags, manifests, platforms of an index), `?digest=` (config, layers with sizes, annotations, referrers such as signatures and SBOMs, the document) | Read |
| Push: upload sessions (monolithic, chunked, mount), manifests | REST | machine-only (Docker CLI); the browse and image pages show the push, `buildx` and pull commands | Deploy |
| Delete a tag | REST (`DELETE manifests/{tag}`) | DockerManage (`DockerManage/Index`) | Delete + Password |
| Delete a manifest (with its tags) | REST (`DELETE manifests/{digest}`) | DockerManage | Delete + Password |
| Delete a blob from an image | REST (`DELETE blobs/{digest}`) | covered by *Remove unused layers* (a blob in use is refused) | Delete |
| Remove unused layers | service | DockerManage | Delete + Password |
| Delete the whole image | service | DockerManage | Delete + Password |
| Repository: create, list, anonymous read, delete | service | Repositories (Docker create form, Browse link), RepositoryEdit, RepositoryDelete | Admin |
| Remote Docker repository: create (URL, user name, password or token, periods, offline, store locally) | service | Repositories > *New remote repository* (format Docker) | Admin + Password |
| Remote Docker: edit upstream, *Test connection* (version check through the token flow), cache table (manifests, images, layer bytes), *Purge cache*, *Purge package* (image, optional tag or digest) | service | RepositoryEdit (`/admin/repositories/{name}`) | Admin + Password |
| Remote Docker: pull through (`GET`/`HEAD` of manifests, layers, tags) | REST | `/docker-images` (the cached images, pull commands), `/docker-images/{image}` | read |
| Virtual Docker repository: create, members, order, deploy repository, *Where would an image come from?* (image, tag or digest) | service | Repositories > *New virtual repository* (format Docker); RepositoryEdit members and preview | Admin |
| Virtual Docker: pull from the first member, merged tags, push to the deploy repository | REST | `/docker-images` (the members' images merged), `/docker-images/{image}` | read / Deploy |
| Docker garbage collection of a repository (dry run, run) | service (`IOciGarbageCollector`) | Admin Operations: *Docker garbage collection: dry run* and *Docker garbage collection* per local or remote Docker repository; scheduled by a retention policy (*Collect garbage*) | Admin, audited |

## Vulnerability scanning

| Capability | Reached by | UI | Gate |
|---|---|---|---|
| Findings of a version (NuGet, npm, Maven, Docker image) | service (`IScanService`) | the *Vulnerabilities* section of `/packages/{id}/{version}`, `/npm-packages/{name}`, `/maven-artifacts/...`, `/docker-images/{image}?digest=` | read |
| Scan a version now | service | `/scan-artifact` (`ScanArtifact/Index`; linked from that section) | signed in, Manage on the package |
| Scan a whole repository | service, operation `vulnerability-scan` | Admin Scanning > repository (*Scan the whole repository now*); Admin Operations | Admin, audited |
| Counts by severity, versions with findings, engines in use | service | Admin Scanning, Admin Scanning > repository | Admin |
| Policy of a repository (warn, block download, block deploy, block unscanned) | service (`IScanPolicyService`) | Admin Scanning > repository | Admin + Password, audited |
| Policy of a project | service | Admin Scanning > project (linked from the project) | Admin + Password, audited |
| 403 with the reason on download and deploy | REST (every format's routes) | package pages explain a refusal; the Upload pages show it | read / Deploy |
| NuGet vulnerability resource | REST | Manage lists the scanner's entries read-only | read |
| Scanning options (`Scanning:*`) | `appsettings.Overrides.json` | Admin Settings (`AdminSettings/Index`) | Admin + Password |

## Repositories (Admin)

| Capability | Reached by | UI | Gate |
|---|---|---|---|
| List, counts, feed URL, links per format | service | Admin Repositories | Admin |
| Filter the list by name fragment, format and kind (`?q=`, `?format=`, `?kind=`; the REST list's parameters, one rule `RepositorySummaryFilter`) | REST `GET /api/v1/repositories` | Admin Repositories: filter form | Admin |
| Create local NuGet / npm / Maven / Docker | service | Repositories create forms | Admin |
| Create remote (URL, credentials, cache periods, offline) | service | Repositories remote form, RepositoryEdit | Admin + Password |
| Create virtual, members, order, upload target | service | Repositories virtual form, RepositoryEdit | Admin |
| Virtual preview, remote connection test | service | RepositoryEdit: Preview, Test | Admin (+ Password for test) |
| Purge remote cache / one package | service | RepositoryEdit | Admin + Password |
| Anonymous read, delete | service | RepositoryEdit, RepositoryDelete | Admin |
| Enable formats | settings | Admin Formats | Admin |

## Access model (Admin)

| Capability | Reached by | UI | Gate |
|---|---|---|---|
| Groups: create, rename, delete, members | `IAccessAdministration` | Groups, GroupDetail | Admin + Password |
| Permission targets: create, edit, delete, grants, restore built-ins | service | Permissions, PermissionDetail | Admin + Password |
| Preview ("who can do what") | service | Permissions / PermissionDetail / ProjectDetail: Preview; Account Access (`AccountPackages/Access`, own access) | Admin |
| Projects: create, members, repositories, delete | service | Projects, ProjectDetail | Admin + Password |
| Audit trail (filter actor, subject, dates) | service | Audit | Admin |

## Accounts, organizations, owners

| Capability | Reached by | UI | Gate |
|---|---|---|---|
| Register, confirm email, sign in (password, 2FA, recovery code, external) | pages | Account pages | anonymous |
| Sign in for a single-page application (password, 2FA ticket + code or recovery code, refresh, exchange, sign out, external provider, cookie sign-in and sign-out, antiforgery token, register / resend confirmation / forgot / reset / confirm without a session) | REST (`/api/ui/session`, `/api/ui/antiforgery`) | the sign-in pages (`Account/Login`, `Account/LoginWith2fa`, `Account/LoginWithRecoveryCode`) | anonymous; same lockout, approval and email rules |
| Forgot / reset password | pages | ForgotPassword, ResetPassword | anonymous |
| Change password / email, delete account | service | Account Settings (`AccountSettings/Settings`) | signed in + Password |
| Sign in with one of several OpenID Connect providers (a button each); link / unlink a provider; re-authenticate with a linked provider | `/api/ui/session/providers`, `/api/ui/session/external/{provider}`, `/api/ui/account/settings/unlink-external` | Login (`Account/Login`), Reauthenticate (`Account/Reauthenticate`), Account Settings > *Sign-in providers* (`AccountSettings/Settings`) | anonymous; link / unlink signed in + Password |
| Two-factor: enable, disable, reset | service | TwoFactor (`AccountSettings/TwoFactor`) | signed in + Password |
| API keys: scopes, glob, expiry, repositories; revoke | service | Account API keys (`AccountSettings/ApiKeysIndex`) | signed in |
| Organizations: create, members | service | Account Organizations (`AccountOrganizations/Organizations`) | signed in |
| Organizations: rename, delete, admin flag | service | Admin Organizations, OrganizationDetail (`AdminOrganizations/Index`, `AdminOrganizations/Detail`) | Admin + Password |
| Package owners add / remove, verified flag | service | Manage (verified: Admin) | Manage |
| Contact owners | service | `/packages/{id}/contact-owners` (`ContactOwners/Index`) | signed in, SMTP |
| Profiles, packages of an owner | pages | `/profiles/{name}` (`Profile/Profile`), Account Packages (`AccountPackages/Packages`); all packages: Admin Packages (`AdminPackages/Index`) | read |
| Approve a self-registered account (`Accounts:RequireApproval`) | CLI `account approve`, service (`AccountApprovalService`) | Admin Users and Admin UserDetail: *Approve* (both show *Awaiting approval*); reject = Delete | Admin + Password, 503 while read-only |
| Users: create, invite, promote (also approves), demote | CLI `account promote\|demote`, service | Admin Users | Admin + Password |
| Reset password, with `--email` | CLI `account reset-password [--email]` | Admin Users: Generate a password (+ optional email) | Admin + Password |
| Send reset link | service | Admin Users | Admin + Password, SMTP |
| Delete user, user's packages and keys | service | DeleteUser, UserDetail, UserPackages (`AdminUsers/DeleteUser`, `AdminUsers/UserPackages`) | Admin + Password |

## Operations and server

| Capability | Reached by | UI | Gate |
|---|---|---|---|
| Validation queue: retry, release, reject | service | Admin Validation (`AdminOperations/Validation`) | Admin (+ Password) |
| Abuse reports: resolve, reopen | service | Admin Abuse (`AdminOperations/Abuse`); Manage for the owner | Admin / Manage |
| Settings (every options section), restart-required marks | `appsettings.Overrides.json` | Admin Settings | Admin + Password |
| Sign-in providers (`Accounts:OpenIdConnectProviders`): list, add, edit, remove named OpenID Connect providers (secret write-only, administrator / group mapping), restart-required | `SignInProviderSettings`, `/api/ui/admin/settings/save-provider`, `remove-provider` | Admin Settings > Accounts > *Sign-in providers* (`AdminSettings/Index`) | Admin + Password, 503 while read-only |
| Logging: default minimum level (live), Windows event log switch and source (restart) | `Serilog` section | Admin Settings > Serilog | Admin + Password |
| Logging: level per source (`Serilog:MinimumLevel:Override`, live): list with where each is set, set, remove (only what the page saved) | `SettingsService.SetLogLevelOverrideAsync` | Admin Settings > Serilog > *Log levels per source* | Admin + Password, 503 while read-only |
| Database copy (copy all data, status) | service | Admin Settings (database section) | Admin + Password |
| Telemetry settings, status, connection test | service | Admin Settings (telemetry section) | Admin + Password |
| Statistics | service | `/statistics` (`Statistics/Index`) | read |
| Read-only gate banner | MaintenanceGate | every page | - |
| Retention policies: create, change, enable, delete (rules, name patterns, schedule) per repository or project | service (`IRetentionAdministration`) | Admin Retention (`/admin/retention`, `/admin/retention/{id}`) | Admin + Password, audited |
| Retention: dry run (what a run would delete), run now, last report, scheduled runs | service (`RetentionEngine`, `RetentionJobs`, `RetentionWorker`) | Admin Retention > policy: *Dry run* (no password), *Run now* (Password); the schedule field | Admin (+ Password), audited, 503 while read-only (run) |
| Legal holds: place, list, lift (artifact or one version) | service (`IRetentionAdministration`) | Admin Retention > Legal holds (`/admin/retention/holds`) | Admin, lifting asks Password, audited |
| Download times (last download per version, for the *unused* rule) | in memory (`ArtifactUsageRecorder`) | shown as *Last download* in a retention report | Admin |
| Replication configurations: create, change, enable, delete (local repository, push or pull, peer address and repository, peer API key, schedule, on deploy, delete propagation, conflict rule) | service (`IReplicationAdministration`) | Admin Replication (`/admin/replication`, `/admin/replication/{id}`) | Admin + Password, audited |
| Replication: test connection, run now, resume an interrupted or failed run, stop, log of runs (status, counts, first failures and conflicts) | service (`ReplicationEngine`, `ReplicationJobs`, `ReplicationWorker`) | Admin Replication > configuration: *Test connection*, *Run now*, *Resume* (Password), *Stop*, the log table | Admin (+ Password), audited, 503 while read-only |
| Cluster status: nodes with health and read-only state, job leases and their holder, the shared store, this node's health checks (database, storage, shared store) | service (`IClusterStatusService`, `ReadinessService`) | Admin Cluster (`AdminOperations/Cluster`, `/admin/cluster`, *Check again*) | Admin |
| High availability settings (`Ha:*`: enable, Redis connection string, node name, key prefix, lease and sync intervals), restart-required marks | settings | Admin Settings > Ha | Admin + Password |
| Health probes for a load balancer: `/health/live`, `/health/ready` (database, storage, shared store) | REST (anonymous, no detail in the body) | Admin Cluster > *This node's health checks* shows the same checks with their detail | - |
| Replication inventory (what a repository holds, for a peer to compare) | service (`IReplicationInventory`) | Admin Replication > configuration: *the local repository holds N items* | Admin |

## Administration REST API (`/api/v1`)

Every operation below has the page that offers the same operation and the same gate: an administrator's API key with the `admin:api` scope (or the global key) in place of the signed-in administrator, and the key owner's password in `X-Confirm-Password` where the page asks for the password. See [Administration REST API](admin-api.md). The list is generated from the controllers; `AdminApiAccessTests` fails when a row is missing or names a page that does not exist.

| Capability | Reached by | UI | Gate |
|---|---|---|---|
| List access-audit entries, newest first, optionally by actor, subject or time | REST `GET /api/v1/audit` | `AdminAudit/Index` | Admin key |
| List the package formats and the kinds and fields each offers | REST `GET /api/v1/formats` | `AdminOperations/Formats` | Admin key |
| List groups with their member counts | REST `GET /api/v1/groups` | `AdminGroups/Index` | Admin key |
| Create a group | REST `POST /api/v1/groups` | `AdminGroups/Index` | Admin key |
| Delete a group with the grants and project roles that name it | REST `DELETE /api/v1/groups/{name}` | `AdminGroups/Detail` | Admin key + password |
| Show a group with its members and the grants naming it | REST `GET /api/v1/groups/{name}` | `AdminGroups/Detail` | Admin key |
| Rename a group | REST `PATCH /api/v1/groups/{name}` | `AdminGroups/Detail` | Admin key |
| Remove a user from a group | REST `DELETE /api/v1/groups/{name}/members/{user}` | `AdminGroups/Detail` | Admin key + password |
| Add a user to a group | REST `PUT /api/v1/groups/{name}/members/{user}` | `AdminGroups/Detail` | Admin key + password |
| Download the OpenAPI 3 description of this API (also `/openapi/{ui,v1,protocols}.json`, browsed at `/scalar`) | REST `GET /api/v1/openapi.json` | `ApiCatalog/Index` | Admin key or admin cookie |
| List the operations this server offers, the repositories each applies to and their last runs | REST `GET /api/v1/operations` | `AdminOperations/Index` | Admin key |
| Show the last run of an operation on a repository | REST `GET /api/v1/operations/{operation}/repositories/{repository}` | `AdminOperations/Index` | Admin key |
| Start an operation on a repository (one that deletes data asks for the password) | REST `POST /api/v1/operations/{operation}/repositories/{repository}` | `AdminOperations/Index` | Admin key + password |
| List permission targets | REST `GET /api/v1/permission-targets` | `AdminPermissions/Index` | Admin key |
| Create a permission target (add grants afterwards) | REST `POST /api/v1/permission-targets` | `AdminPermissions/Index` | Admin key |
| Preview which repositories (and whether a package) a set of patterns selects; saves nothing | REST `POST /api/v1/permission-targets/preview` | `AdminPermissions/Index` | Admin key |
| Re-create the built-in groups and permission targets that are missing | REST `POST /api/v1/permission-targets/restore-built-ins` | `AdminPermissions/Index` | Admin key + password |
| Delete a permission target and its grants | REST `DELETE /api/v1/permission-targets/{name}` | `AdminPermissions/Detail` | Admin key + password |
| Show a permission target with its grants | REST `GET /api/v1/permission-targets/{name}` | `AdminPermissions/Detail` | Admin key |
| Replace a permission target's repository and package patterns | REST `PUT /api/v1/permission-targets/{name}` | `AdminPermissions/Detail` | Admin key + password |
| Give a user, group or organization actions through a permission target (none removes the grant) | REST `PUT /api/v1/permission-targets/{name}/grants` | `AdminPermissions/Detail` | Admin key + password |
| List projects with their repository and member counts | REST `GET /api/v1/projects` | `AdminProjects/Index` | Admin key |
| Create a project | REST `POST /api/v1/projects` | `AdminProjects/Index` | Admin key |
| Delete a project with its members, targets and retention/scan policies (its repositories stay) | REST `DELETE /api/v1/projects/{name}` | `AdminProjects/Detail` | Admin key + password |
| Show a project with its repositories, members and permission targets | REST `GET /api/v1/projects/{name}` | `AdminProjects/Detail` | Admin key |
| Give a user or group a role in a project | REST `PUT /api/v1/projects/{name}/members` | `AdminProjects/Detail` | Admin key + password |
| Remove a user or group from a project | REST `DELETE /api/v1/projects/{name}/members/{type}/{principal}` | `AdminProjects/Detail` | Admin key + password |
| Take a repository out of a project | REST `DELETE /api/v1/projects/{name}/repositories/{repository}` | `AdminProjects/Detail` | Admin key + password |
| Put a repository in a project (it leaves any project it was in) | REST `PUT /api/v1/projects/{name}/repositories/{repository}` | `AdminProjects/Detail` | Admin key + password |
| List repositories (filter by format, kind or name) | REST `GET /api/v1/repositories` | `AdminRepositories/Index` | Admin key |
| Create a local, remote or virtual repository of any format (remote asks for the password) | REST `POST /api/v1/repositories` | `AdminRepositories/Index` | Admin key + password |
| Delete an empty repository (refused for default, a repository with packages, or a virtual member) | REST `DELETE /api/v1/repositories/{name}` | `AdminRepositories/DeleteRepository` | Admin key |
| Show a repository with its upstream, cache or members | REST `GET /api/v1/repositories/{name}` | `AdminRepositories/Edit` | Admin key |
| Switch a repository's anonymous read (a remote repository asks for the password) | REST `PATCH /api/v1/repositories/{name}` | `AdminRepositories/Edit` | Admin key + password |
| Show who holds which action in a repository, and why | REST `GET /api/v1/repositories/{name}/access` | `AdminRepositories/Edit` | Admin key |
| Change a remote repository's upstream and settings | REST `PUT /api/v1/repositories/{name}/remote` | `AdminRepositories/Edit` | Admin key + password |
| Show what a remote repository stores and remembers | REST `GET /api/v1/repositories/{name}/remote/cache` | `AdminRepositories/Edit` | Admin key |
| Purge a remote repository's cache (deletes what it stored) | REST `POST /api/v1/repositories/{name}/remote/purge` | `AdminRepositories/Edit` | Admin key + password |
| Purge one cached package of a remote repository | REST `POST /api/v1/repositories/{name}/remote/purge-package` | `AdminRepositories/Edit` | Admin key + password |
| Test a remote repository's upstream with unsaved settings (saves nothing) | REST `POST /api/v1/repositories/{name}/remote/test` | `AdminRepositories/Edit` | Admin key + password |
| Replace a virtual repository's members, their order and its deploy repository | REST `PUT /api/v1/repositories/{name}/virtual` | `AdminRepositories/Edit` | Admin key |
| Show where a package version would come from in a virtual repository (fetches and stores nothing) | REST `POST /api/v1/repositories/{name}/virtual/preview` | `AdminRepositories/Edit` | Admin key |
| List legal holds, optionally of one repository | REST `GET /api/v1/retention/holds` | `AdminRetention/Holds` | Admin key |
| Place a legal hold on an artifact (retention never deletes it) | REST `POST /api/v1/retention/holds` | `AdminRetention/Holds` | Admin key |
| Lift a legal hold | REST `DELETE /api/v1/retention/holds/{key}` | `AdminRetention/Holds` | Admin key + password |
| List retention policies with their last run | REST `GET /api/v1/retention/policies` | `AdminRetention/Index` | Admin key |
| Create a retention policy | REST `POST /api/v1/retention/policies` | `AdminRetention/Index` | Admin key + password |
| Delete a retention policy (what it deleted stays deleted) | REST `DELETE /api/v1/retention/policies/{key}` | `AdminRetention/Policy` | Admin key + password |
| Show a retention policy with its last run and report | REST `GET /api/v1/retention/policies/{key}` | `AdminRetention/Policy` | Admin key |
| Replace a retention policy's scope and rules | REST `PUT /api/v1/retention/policies/{key}` | `AdminRetention/Policy` | Admin key + password |
| Start a dry run of a policy: the report of what a run would delete (nothing is deleted) | REST `POST /api/v1/retention/policies/{key}/dry-run` | `AdminRetention/Policy` | Admin key |
| Show the last run (or dry run) of a policy with its report | REST `GET /api/v1/retention/policies/{key}/run` | `AdminRetention/Policy` | Admin key |
| Start a run of a policy: the versions it selects are deleted | REST `POST /api/v1/retention/policies/{key}/run` | `AdminRetention/Policy` | Admin key + password |
| List replications with their last run | REST `GET /api/v1/replication/configs` | `AdminReplication/Index` | Admin key |
| Create a replication | REST `POST /api/v1/replication/configs` | `AdminReplication/Index` | Admin key + password |
| Show a replication | REST `GET /api/v1/replication/configs/{key}` | `AdminReplication/Config` | Admin key |
| Replace a replication's settings | REST `PUT /api/v1/replication/configs/{key}` | `AdminReplication/Config` | Admin key + password |
| Delete a replication with its log (what it copied stays where it is) | REST `DELETE /api/v1/replication/configs/{key}` | `AdminReplication/Config` | Admin key + password |
| Test the connection to a peer with the given settings (nothing is saved; a stored key is used while the address is unchanged) | REST `POST /api/v1/replication/test` | `AdminReplication/Config` | Admin key + password |
| Start a run of a replication | REST `POST /api/v1/replication/configs/{key}/run` | `AdminReplication/Config` | Admin key + password |
| Start a run that continues the last interrupted or failed run of a replication | REST `POST /api/v1/replication/configs/{key}/resume` | `AdminReplication/Config` | Admin key + password |
| Ask the running replication to stop (it ends as interrupted and can be resumed) | REST `POST /api/v1/replication/configs/{key}/stop` | `AdminReplication/Config` | Admin key |
| List the runs of a replication, newest first (status, counts, the first failures and conflicts) | REST `GET /api/v1/replication/configs/{key}/runs` | `AdminReplication/Config` | Admin key |
| Show one run of the replication log | REST `GET /api/v1/replication/runs/{key}` | `AdminReplication/Config` | Admin key |
| List what a repository holds in the form replication compares (what a peer asks; summary=true: only the count) | REST `GET /api/v1/replication/inventory/{repository}` | `AdminReplication/Config` | Admin key |
| Show whether scanning is on and every repository's numbers and policy | REST `GET /api/v1/scanning` | `AdminScanning/Index` | Admin key |
| Show a project's scanning policy | REST `GET /api/v1/scanning/projects/{name}/policy` | `AdminScanning/Project` | Admin key |
| Set a project's scanning policy (all thresholds empty removes it) | REST `PUT /api/v1/scanning/projects/{name}/policy` | `AdminScanning/Project` | Admin key + password |
| Show a repository's scan numbers, its own policy and the last scan | REST `GET /api/v1/scanning/repositories/{name}` | `AdminScanning/Repository` | Admin key |
| Set a repository's scanning policy (all thresholds empty removes it) | REST `PUT /api/v1/scanning/repositories/{name}/policy` | `AdminScanning/Repository` | Admin key + password |
| List the scanned versions with findings of at least a severity, worst first | REST `GET /api/v1/scanning/repositories/{name}/results` | `AdminScanning/Repository` | Admin key |
| Scan every artifact of a repository in the background | REST `POST /api/v1/scanning/repositories/{name}/scan` | `AdminScanning/Repository` | Admin key |
| List users, optionally by a name fragment | REST `GET /api/v1/users` | `AdminUsers/Index` | Admin key |
| Create a user (with a given or a generated password, returned once) | REST `POST /api/v1/users` | `AdminUsers/Index` | Admin key + password |
| Show a user with groups, project roles, organizations and API keys | REST `GET /api/v1/users/{name}` | `AdminUsers/Detail` | Admin key |
| Show what a user may do in each repository, and the grants behind it (for one package id with ?package=) | REST `GET /api/v1/users/{name}/access` | `AdminUsers/Detail` | Admin key |
| Make a user an administrator or take the flag away (never the last administrator) | REST `PUT /api/v1/users/{name}/administrator` | `AdminUsers/Index` | Admin key + password |
| Remove a user from a group | REST `DELETE /api/v1/users/{name}/groups/{group}` | `AdminUsers/Detail` | Admin key + password |
| Add a user to a group | REST `PUT /api/v1/users/{name}/groups/{group}` | `AdminUsers/Detail` | Admin key + password |
| Revoke one of a user's API keys | REST `DELETE /api/v1/users/{name}/keys/{key}` | `AdminUsers/Detail` | Admin key + password |

## Audit method and gaps closed

Standing rule: `UiCoverageInventoryTests` (controller, UI action, CLI command each need a row) and `AdminApiAccessTests` (each REST operation names an existing page). The last full audit compared, one by one: every controller route, every `IFormatModule`/administration/service method an operator reaches, every CLI command and option, every registered options type (the Settings catalog is built from `AddBaGetOptions`, so a new section appears by itself), every `?handler=` action, and the query parameters of each REST list and protocol endpoint with the page that serves it.

| Gap found | Closed by |
|---|---|
| REST repository list filters by format, kind and name; the page listed everything | Admin Repositories filter form (shared `RepositorySummaryFilter`) |
| OCI referrers API filters by `artifactType`; the image page showed all referrers | *Referrers* filter form on the manifest page |

No other operation lacked a page or a field. Items the UI cannot do are listed below.

## Not in the UI, on purpose

| Item | Why |
|---|---|
| `--urls`, host arguments | process start, not a runtime operation |
| Registration, flat container, symbol download, tarball bodies | machine formats; the pages link the downloads |
| Serilog sinks (`WriteTo`, file path, retention) | arrays of package-specific objects, read once at start; edit the file or the environment ([Logging](configuration.md#logging)) |
| `Kestrel` endpoints, `SettingsOverridesPath` | read before the settings file exists (listening address, where the file lives) |
| External login link / unlink | accounts are created by the first external sign-in; there is no link-later operation on the server |
