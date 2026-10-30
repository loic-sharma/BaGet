# Virtual repositories

A **virtual** repository presents several repositories of one format (local, [remote](remote-repositories.md) or other virtual ones) as one feed at `/r/{name}/v3/index.json` (npm: `/r/{name}/npm/`, see [npm virtual repositories](#npm-virtual-repositories); Maven: `/r/{name}/maven/`; Docker: `/v2/{name}/{image}`). Clients configure one source; the server decides which member answers. It stores nothing itself.

## Create and edit

Administrators: `/admin/repositories` > *New virtual repository* (a name and the **format**, NuGet, npm, Maven or Docker; it starts empty), then `/admin/repositories/{name}`. Needs a relational database and accounts, like all [repository pages](repositories.md#admin-pages). Not available with Azure Table or `Search:Type` = `AzureSearch` (only `default` exists there).

| Control | Does |
|---|---|
| *Anonymous read* (create form, default on) | Whether requests without credentials may read the virtual repository. A member is read only when the caller may read that member, so this never exposes a closed member |
| Members table | Resolution order, top to bottom. *Up* / *Down* reorder, *Remove* drops a member. Saved at once, audited |
| *Add a member* | Any other repository **of the same format** whose own members do not lead back to this one; added at the end |
| Deploy repository | The member that receives uploads: a local member, or a virtual member that itself deploys to a local one. A remote repository cannot be one. *None* = the repository takes no uploads |
| *Where would a package come from?* | Package id (npm: package name) + version: asks each effective member in order and shows what it found (*stored here*, *its upstream has it and it would be fetched*, *not found*, *its upstream could not be asked*, *not asked: an earlier member answers*) and which member answers. Fetches and stores nothing. It uses the rules of a real request: each member's stored copy, then its own upstream (a remote repository's, or the one `default` fetches through while `Mirror:Enabled`); an upstream in offline mode shows *not stored; its upstream is in offline mode*, a remembered 404 counts as not found, and later members' upstreams are not asked once one answers |

Rules: at most 50 members; a repository is listed once; a member must exist and be of the virtual repository's format (a NuGet virtual repository takes no npm, Maven or Docker member, and so on); **nesting** is allowed, a nested virtual member is expanded in place (each repository counts once, at its first place), and a change that would make a virtual repository contain itself is refused with the reason. A repository that a virtual repository lists cannot be deleted until it is removed there (the delete page says where). Edits are refused with `503` while the server is read-only for maintenance. Members are stored by name in `Repository.Settings` (no schema change).

## What a client sees

| Request | Result |
|---|---|
| Download, `.nuspec`, readme, icon of a version | The **first member**, in order, that has that version. A remote member fetches and stores it (in itself) as it does when read directly; a member that fails (its upstream is down) is logged and skipped, so a later member can answer |
| Version list, registration index | The **union** of the members' versions; each version's data comes from the first member that has it. (A package id in a local member and on nuget.org therefore lists the versions of both) |
| Search | Merged, one result per package id: the first member's entry, the versions of all members (highest first, the highest reported) and the summed downloads. Order is total and independent of the page: *relevance* = id (case-insensitive), *downloads* = summed downloads then id; `skip`/`take` never repeat or drop a package. *Last updated* is not offered. Each member is read only as far as the page needs (its first `skip + take + 1` results, 500 per call, at most 10,000, so a first page is one small call per member); a member of at most 2,000 results is read completely. While every member was read completely `totalHits` is the exact number of distinct ids; otherwise it is an estimate (ids seen plus each member's unseen remainder, an upper bound), results deeper than 10,000 per member are cut off, and with *downloads* an id held by several members is summed from the entries inside each member's window, so it can rank slightly low at a window edge |
| Autocomplete, version enumeration, dependents | The same merge (ids by name; versions ascending; dependents by summed downloads, top 20) |
| Symbols | The first member that stores the PDB |
| V2 feed (`/r/{name}/api/v2`) | `FindPackagesById`, `Packages(Id,Version)`, `Packages()` and `Search()` read the members' stored packages the same way (the V2 feed never asks an upstream, here or elsewhere) |
| Vulnerability index | The advisories of the readable members, each once |
| Scanning policy (download refusals) | The first member that has a scan result for the version decides, under that member's policy; a virtual repository has no results or policy of its own |
| Catalog | **Not served** (`404`, and the service index omits `Catalog/3.0.0`): a virtual repository records nothing, and a merged log would have no commit order a client could resume from. Use the members' catalogs |
| Service index | Like any repository's, with every URL inside `/r/{name}/` |
| Web pages | *Packages*, a package's page, *Upload* and *Browse* work; *Statistics*, *Report abuse*, *Manage package* and *Contact owners* answer `404` (a virtual repository has no owners, reports or counters of its own: use the member's page) |

Every link in an answer (registration, search, V2 entries, content) points into the virtual repository, so clients keep resolving through it.

## Writes

A write to a virtual repository **is a write to its deploy repository**: the request is moved there before anything else, so the access model, package ownership, validation and the catalog all apply to that repository.

| Request | Result |
|---|---|
| Push, symbol push, unlist, relist, deprecation, vulnerabilities (`PUT`/`POST`/`DELETE`), validation status | Handled in the deploy repository; needs **Deploy** (or the action asked) **there**. Deploy on the virtual repository itself is never granted (not even to administrators) and never needed. Without a deploy repository: `403`. An unlist or delete of a package stored in another member finds nothing in the deploy repository |
| Browser upload (`/r/{name}/upload`) | Stores in the deploy repository (the page says which); offered to those who can read the virtual repository and deploy to its deploy repository |

## Access

* Reading needs **Read on the virtual repository** (anonymous read, grants and project roles work as for any repository: [access control](access-control.md)).
* A request is answered **only from the members the caller may read** (per package too, by the grants' include/exclude patterns). A private member contributes nothing for a caller without Read on it: no error, no hint; a package only it has is `404`.
* Search and the V2 listing filter by repository, not by package pattern, like everywhere else.
* The *Effective access* table of a virtual repository shows *Deploy* as decided by the deploy repository.

## npm virtual repositories

A virtual repository of format npm merges npm repositories (`/r/{name}/npm/`). Members are read in their own scope (their storage, upstream and access rules apply), only those the caller may read, and a member that fails (an upstream that is down) is logged and skipped.

| Request | Result |
|---|---|
| Packument (full and install-only) | The members' packuments **merged**: `versions` is the union, each version from the **first member** that has it (semantic versions compared without build metadata); `dist-tags` take, per tag, the **first member that has the tag**, so `latest` follows the member order and not the highest version (a tag whose version the merge does not hold is dropped; `latest` falls back to the highest version if no member has it); `time` is the union with the earliest `created` and the latest `modified`; description, readme and the other fields come from the first member that has them. Every tarball address points into the virtual repository |
| Tarball | The first member that has it; a remote member downloads and stores it as it does when read directly |
| Dist-tags (`GET`), `npm view`, package pages | The same merge |
| Search | Merged by package name (the first member's entry), ordered by name; each member is read for the first `skip + take` hits (at most 1000); remote members contribute the packages they stored. `total` is exact while every member was read completely, else an upper bound |
| `npm publish`, dist-tag, deprecate, unpublish | **Moved to the deploy repository** before authorization: it needs the matching action *there*, ownership there, and the unpublish window of `Npm:UnpublishWindow` applies there. Without a deploy repository: `403`. The service (`INpmRegistry`) does the same for callers that are not routes |
| `npm login`, `whoami`, `ping`, `audit` | As for any repository; login is answered by the virtual repository (not moved to the deploy repository) and its token also covers the members and the deploy repository |
| Web pages | *Browse*, a package's page and *Upload* (to the deploy repository) work; *Manage* answers `404` (use the member's page) |

The *Where would a package come from?* preview asks each npm member: *stored here* (the version's tarball is stored), *not stored yet; its upstream has it* (a remote member, from its remembered or fresh packument), *not found*, *its upstream could not be asked*, *offline*.

## Maven virtual repositories

A virtual repository of format Maven merges Maven repositories (`/r/{name}/maven/`): files from the first member that has them, artifact `maven-metadata.xml` merged (versions union; `latest` and `release` follow the member order), writes moved to the deploy repository. Details in [Maven repositories](maven-repositories.md#virtual-maven-repositories). The preview takes `group:artifact` and a Maven version and asks each member (*stored here*, *would be fetched*, *not found*...).

## Docker virtual repositories

A virtual repository of format Docker merges Docker repositories (`docker pull host/{name}/{image}`): manifests and layers from the first member that has them, tag lists merged, a push moved to the deploy repository (a `403 DENIED` without one). Only the members the caller may pull from are asked; a registry token names the members it was issued for. Details in [Docker registry](docker-registry.md#virtual-docker-repositories). The preview takes an image name and a tag or digest and asks each member (*stored here*, *would be fetched*, *not found*...) with a `HEAD` and nothing stored.

## `default` and the mirror

`default` keeps its URL (`/`). With `Mirror:Enabled`, it behaves as a virtual repository of two members: its own packages, then the `mirror` remote repository (see [the mirror](remote-repositories.md#the-mirror)). What it fetches is stored **in `default`'s own rows**, exactly as before; `mirror` itself stays empty unless it is read at `/r/mirror/`. This is not a stored virtual repository (nothing to migrate, nothing to edit). To compose your own:

* a virtual repository with `default` as a member reads `default` as it reads itself, mirror fall-through included;
* a virtual repository with `mirror` as a member reads the mirror's own cache (separate from what `default` stored).

## Not covered

No per-member path rules (include/exclude patterns), no merged catalog (only NuGet has one), no *last updated* sort, and the V2 feed ignores `$filter`/`$orderby` as everywhere. Per-member resolution state of remote members (memory, health) is per node, as for [remote repositories](remote-repositories.md).
