# Repositories

A server holds one or more named repositories. Each has its own packages, owners, catalog and validations.

| Repository | Feed |
|---|---|
| `default` | `/v3/index.json` (unchanged; every existing install is this one) |
| any other | `/r/{name}/v3/index.json` |

Local, [remote](remote-repositories.md) (upstream cache) and [virtual](virtual-repositories.md) (ordered group of members) repositories can be created for NuGet, [npm](npm-registry.md), [Maven](maven-repositories.md) and [Docker](docker-registry.md) (the [format layer](formats.md) is how formats plug in). A Docker repository is addressed as `/v2/{name}/{image}` at the server root, not under `/r/{name}/`.

## Web pages

The web pages work in every repository: the package list, package, statistics, report-abuse, manage, contact-owners, profile and upload pages are at their usual address for `default` and under `/r/{name}/` for any other (`/r/{name}/packages`, `/r/{name}/upload`, ...). Links on a page stay in the repository being browsed.

| Where | What |
|---|---|
| Header | A *Repository* menu appears on those pages when more than one repository is readable; it goes to the front page of the chosen one. Hidden with a single repository, and always with Azure Table or `Search:Type` = `AzureSearch` (only `default`) |
| Upload | `/r/{name}/upload` publishes to `{name}`; the page lists the repositories you can publish to and shows the push commands of the one open |
| My packages | The packages you own, grouped by repository |
| `/admin/repositories` | *Browse* and *Upload* links per repository |
| Service index | Each repository advertises its own `PackageDetailsUriTemplate` and `ReportAbuseUriTemplate` |

## Admin pages

Administrators manage them at `/admin/repositories` (menu: *Repositories*). Other users and anonymous requests get `404` or the sign-in page.

| Page | Does |
|---|---|
| `/admin/repositories` | Lists name, format, type, package count and feed address (with a copy button) and the links of its format (npm: *Browse*, *Upload*, *Registry*; Maven: *Browse*, *Upload*, *Repository*; Docker: *Browse* and the `v2/{name}/` registry address); creates a local, a [remote](remote-repositories.md) or a [virtual](virtual-repositories.md) repository (the remote and virtual forms ask for the format: NuGet, [npm](npm-registry.md), [Maven](maven-repositories.md) or [Docker](docker-registry.md)), or a local one of another format |
| `/admin/repositories/{name}` | Shows the feed and who effectively holds each action (see [Access control](access-control.md#admin-pages)); edits *Anonymous read*; for a remote repository also its upstream, *Test connection*, the cache and *Purge* ([details](remote-repositories.md)); for a virtual one its members, deploy repository and *Where would a package come from?* ([details](virtual-repositories.md)); a member lists the virtual repositories it belongs to |
| `/admin/repositories/{name}/delete` | Deletes a repository that holds **no packages** (a remote one: purge its cache first) |

Other administrator pages work across all repositories:

| Page | Does |
|---|---|
| `/admin/operations` | Runs *Rebuild the catalog*, *Import download counts*, the Docker garbage collection (dry run and run) and the vulnerability scan for a chosen repository as background jobs; shows each one's last status |
| `/admin/validation` | Pending and quarantined submissions of every repository: retry, release (index anyway, audited), reject (see [configuration](configuration.md#validate-packages-before-serving-them)) |
| `/admin/abuse` | Abuse reports of every repository (unresolved by default, `?all=true` for all): resolve or reopen; links each package to its Manage page in its own repository |

Rules:

* Names: 1 to 64 lowercase letters, digits, `-`, `.`, `_`; no leading or trailing `.`, no `..`; not a reserved word (`admin`, `api`, `r`, `v3`, ...). Invalid names are refused with the reason.
* A name is never changed after creation (it is in URLs and storage paths).
* `default` cannot be deleted.
* A repository that a virtual repository lists as a member cannot be deleted until it is removed there.
* A repository that still holds packages, or submissions awaiting validation, cannot be deleted; delete the packages first and wait for the validations. Deleting removes its history rows too (catalog entries, owners, validations, abuse reports, API keys limited to it alone) and every file under `repos/{name}/` in storage (symbols, catalog files, staged uploads), so a repository re-created under the same name starts empty.
* Writes are refused with `503` while the server is read-only for maintenance (for example during a database copy).
* *Anonymous read* is enforced: off, anonymous callers get `401` (feeds) or the sign-in page, and others need a Read grant of their own. See [Access control](access-control.md).
* Creating needs a relational database and a repository-aware search: with Azure Table, or with `Search:Type` = `AzureSearch` (one global index), only `default` exists and other repositories already in the database are not served.
* The `AddRepositories` migration creates the `default` row itself, so it exists even when migrations are applied outside the app.
