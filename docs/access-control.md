# Access control

Who may **read** a repository and who may **change** packages in it. Two layers, both required for a write:

| Layer | Decides | Where |
|---|---|---|
| Access model | Which repositories and package IDs a caller may read, deploy to, delete from, annotate, manage | this page |
| Package ownership | Which existing package IDs a user may change | [Accounts](configuration.md#accounts-package-owners-and-api-keys) |

Needs a relational database (SQLite, SQL Server, PostgreSQL, MySQL). With Azure Table there is no access model: everyone
reads, any authenticated caller writes subject to ownership, as before.

## Model

| Entity | Meaning |
|---|---|
| Group | A named set of users. Built-in: **Everyone** (every caller, anonymous included) and **Authenticated** (every caller with valid credentials); their membership is implicit |
| Permission target | Repository patterns (names or `*` globs), package-ID include patterns (default `*`) and exclude patterns, optionally owned by a project |
| Grant | A user, group or organization and the actions it holds through a target |
| Project | Groups repositories (`Repository.ProjectKey`); members hold a role: *Viewer* = Read; *Developer* = Read, Annotate, Deploy; *Admin* = all |
| Audit entry | One row per change to any of the above, and per *Anonymous read* switch |

| Action | Allows |
|---|---|
| `Read` | Feed, symbols, catalog, registrations, package pages of the repository; npm: packuments, tarballs, search, the browse and package pages |
| `Annotate` | Deprecation and vulnerabilities; npm: `npm deprecate` |
| `Deploy` | Push new packages and versions. Never held on a [remote repository](remote-repositories.md) (not by administrators either): it only holds what the server cached. Never held on a [virtual repository](virtual-repositories.md#writes) either: a push to it is judged on its deploy repository |
| `Delete` | Unlist, relist, delete; Docker: delete tags, manifests and blobs (pull = Read, push = Deploy; the image name is the package id of the patterns, see [Docker registry](docker-registry.md#access)); npm: unpublish (a version younger than `Npm:UnpublishWindow` needs only `Deploy`, see [npm registry](npm-registry.md#unpublish-policy)) |
| `Manage` | The package's Manage page: owners and abuse reports need it; opening the page takes any of Manage, Delete or Annotate. Unlist, relist and delete need `Delete`, deprecation and vulnerabilities `Annotate` (what the protocol asks). An owner needs the action as well as ownership; administrators hold them everywhere |

Actions are independent: `Deploy` does not imply `Read`. A caller holds an action when **any** applicable grant carries it.
Server administrators and the global API key hold every action everywhere. Include/exclude patterns narrow package-level
checks only: a repository-level question (service index, search) is allowed when some package could match.

## Upgrade defaults

The migration seeds what the server did before the model, so nothing changes until an administrator edits it:

| Target | Repositories | Principal | Actions |
|---|---|---|---|
| Anyone reads every repository | `*` | Everyone | Read |
| Authenticated users deploy | `*` | Authenticated | Annotate, Deploy, Delete, Manage |

Ownership still applies inside Deploy, Delete, Annotate and Manage. Both targets can be deleted and restored
(`IAccessAdministration.RestoreBuiltInsAsync`).

## Anonymous read

`Repository.AnonymousRead` (the *Anonymous read* switch of `/admin/repositories/{name}`) gates the **Everyone** group's
`Read` on that repository:

| Switch | Effect |
|---|---|
| on (default) | Everyone's Read grants apply. If no target grants Everyone Read on the repository any more, switching on creates the target `Anonymous read {name}` |
| off | Everyone's Read does not apply there, whether it comes from a permission target or from a project role held by Everyone (its other actions still do); callers need a Read grant of their own (user, group, organization or project role) |

## What a client sees

| Caller | Private repository |
|---|---|
| Anonymous | `401` with `WWW-Authenticate: Basic realm="BaGet"` on every protocol route (`/` and `/r/{repo}/`) whatever the HTTP method; the routes listed below skip the check and authenticate with an API key |
| Signed in, no Read | `404`, as for an unknown repository or package |
| With Read | served |

Pages follow the same check, and a page of one package (`/packages/{id}/...`: details, report abuse, manage, contact owners) is also checked against the caller's include/exclude patterns, so a hidden package answers `404` there as on its registration: an anonymous visitor is sent to sign in, a signed-in user without Read gets `404`, and the
repository switcher and search lists only show readable repositories. Search results are filtered per repository, not
per package: a Read grant narrowed by include/exclude patterns protects the per-package routes (registration, content),
but `search`, `autocomplete` and the V2 listings of a repository the caller may read show all its packages.

Routes that skip the Read check (they authenticate with an API key and are authorized per repository and package by Deploy, Delete or Annotate, so a Deploy-only *drop box* works):

| Route | Skips |
|---|---|
| Push (`PUT`/`POST` `api/v2/package`), symbol upload, delete, relist, deprecation and vulnerability `PUT`/`DELETE` | write methods only; a `GET`, `HEAD` or `OPTIONS` to them is still checked |
| Validation status (`GET v3/package/{id}/{version}/validation`) | always: the pusher sees what became of its push without Read; the controller itself needs an API key with Push or Manage on that package in that repository (`401` without a key, `403` otherwise) |

The browser upload page follows the same rule: it needs **Deploy** on the repository (not Read) for a signed-in user, so a Deploy-only user can upload, one with Read only gets `404`, and the repository picker lists the same set. Its "View it" link appears only when the user may read the package.

## Upgrading

`Repository.AnonymousRead` could be saved as off before access control existed, when it did nothing; now it makes the repository private. The saved setting is kept. At every startup (migrations enabled) a Warning names each non-default repository with the switch off and no Read grant for anyone but administrators (target grants and project roles counted; the seeded targets give Everyone Read, which the switch gates), so the change is visible. Switch *Anonymous read* on, or grant Read, to silence it.

## Client setup

The password is an **API key** (an account's key, or the global `ApiKey`); the user name is the key's owner (any name for
the global key).

| Client | Setup | Checked |
|---|---|---|
| `dotnet` (restore, search, push) | `dotnet nuget add source https://host/r/second/v3/index.json -n second -u alice -p <key> --store-password-in-clear-text` (add `--allow-insecure-connections` for `http`). Without credentials: `NU1301` 401. Push keeps using `--api-key` | yes, .NET SDK 10.0.402 against a running host |
| `nuget.exe` | `nuget sources add -Name second -Source <url> -Username alice -Password <key> -StorePasswordInClearText`. Without credentials it asks its credential providers, then fails with 401 | yes, 6.2.1 (`install`; its `list` has no V3 support) |
| `NuGet.Config` | `<packageSourceCredentials><second><add key="Username" value="alice" /><add key="ClearTextPassword" value="<key>" /></second></packageSourceCredentials>` | yes (what the two above write) |
| Visual Studio | *Tools > Options > NuGet Package Manager > Package Sources*: user name and password on the source; it also prompts on the 401 challenge | no (same NuGet client code as `dotnet`) |
| PowerShellGet (V2 feed `/r/second/api/v2`) | `Register-PSRepository ... -SourceLocation <v2 url>`, then `-Credential` on `Find-Module` / `Install-Module` | no: needs the NuGet provider bootstrap (internet). The V2 routes answer 401 / Basic exactly like V3 (`PrivateRepositoryTests`, real host: 401 anonymous, 200 with Basic) |
| Symbols (Visual Studio) | Same credentials on the symbol server entry (`/api/download/symbols`) | route challenged (401 + Basic) anonymous, served with Basic; the debugger itself not run |

A wrong password, or the right key with another user name, is `401` again.

## Request credentials

Reads accept, in order: `Authorization: Basic` (password = API key), `X-NuGet-ApiKey`, then the browser session.
Credentials that do not authenticate count as none: an open repository stays readable, a private one answers `401`.
Writes are unchanged: `X-NuGet-ApiKey`, authorized per package (`401` no key, `403` not permitted).

## Repository-scoped API keys

A key can be limited to some repositories (`ApiKeyRepository`, picked on `/account/apikeys` among the repositories the
user can read; hidden where only `default` exists). Existing and new keys with nothing ticked work in every repository.
Effective rights = the key's scopes and package pattern, intersected with its repositories and with what its user may
do there (this model, then ownership).

| Request with a limited key | Outcome |
|---|---|
| Write (push, unlist, delete, deprecate) in a repository outside the limit | `403`, also for an administrator's key |
| Read of a repository outside the limit | judged as an anonymous caller: an open repository is served, a private one answers `404` |
| Anything inside the limit | the user's own permissions decide |

Deleting a repository removes it from the keys limited to it and deletes keys that were limited to it alone (they would
otherwise become unlimited). The user list in `/admin/users/{name}` shows each key's repositories.

## Cost and cache

The model is held in memory as a snapshot, rebuilt after any committed change to it (`AccessModelSaveChangesInterceptor`
bumps `AccessModelVersion`) and at most every 30 s (other instances catch up within that time). A check against
50 targets and 20 groups costs a few microseconds (`AccessEvaluatorTests`); a user's group and organization membership
is loaded on first use and kept with the snapshot.

## Admin pages

Menu entries *Groups*, *Permission targets*, *Projects*, *Access audit* (administrators) and *My access* (every signed-in
user). Everything answers `404` to anyone else, and, with Azure Table (no access model), sends administrators to
`/admin/access-unavailable`, which says so. Every change is audited, takes effect on the next request, asks for **your
password again** unless it only creates an empty group, target or project (or renames a group), and is refused with `503` while the
server is read-only for maintenance.

| Page | Does |
|---|---|
| `/admin/groups`, `/admin/groups/{name}` | create, rename, delete (not the built-ins); add members through a user-name search, remove them; lists the grants naming the group |
| `/admin/permissions`, `/admin/permissions/{name}` | create targets and edit their repository / include / exclude patterns; **Preview** shows the repositories the patterns match and a **test box** answers "does `Contoso.Lib` match?" (and which pattern decided), on unsaved edits too; grants per user, group or organization with one box per action; delete; restore the built-in targets |
| `/admin/projects`, `/admin/projects/{name}` | create; assign repositories (a repository is in one project); members with a role (user or group); the project's own targets; delete (keeps the repositories) |
| `/admin/users/{name}` | the user's groups (add/remove), project roles and **effective permissions per repository**, each with the targets and roles that grant them; a package ID box applies include/exclude patterns |
| `/admin/repositories/{name}` | adds who effectively holds Read, Annotate, Deploy, Delete and Manage, the project and the anonymous-read switch |
| `/admin/audit` | the audit log, newest first (200 at most), filtered by actor, subject and UTC dates (`yyyy-MM-dd`, inclusive); also lists the [retention](retention.md) policy, hold and run entries (`retention.*`) |
| `/account/access` | your own effective permissions in each repository where you hold something (nothing is shown about repositories you cannot use), with the reasons; a package ID box |

**Project administrators** (role *Admin*) are admitted to their own project's page and to its targets, with the limits
below; they see nothing of other projects, groups or the global targets.

| Who | May |
|---|---|
| Server administrator | everything above |
| Project administrator | set and remove the project's members, create the project's targets, edit their patterns and grants, delete them |
| Project administrator may **not** | assign repositories to the project (it would let them take any repository), delete the project, or touch targets of other projects or global ones |

A project target applies only to the project's repositories whatever its patterns say, so a project administrator cannot
reach beyond them; the previews show only those repositories.
