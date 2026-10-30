# Remote repositories

A **remote** repository caches another feed: a NuGet feed (nuget.org, another BaGet, Artifactory, Azure Artifacts, ...) an [npm registry](#npm-remote-repositories) (registry.npmjs.org, Verdaccio, Artifactory, another BaGet, ...) a [Maven repository](#maven-remote-repositories) (Maven Central, Artifactory, Nexus, another BaGet, ...) or a [Docker registry](#docker-remote-repositories) (Docker Hub, GHCR, Quay, ...). A package it does not hold is fetched from the upstream the first time it is requested, stored, and served from there afterwards. Its feed is `/r/{name}/v3/index.json` (npm: `/r/{name}/npm/`, Maven: `/r/{name}/maven/`, Docker: `/v2/{name}/{image}`) like any repository's; clients cannot push to it.

## Create and edit

Administrators: `/admin/repositories` > *New remote repository* (pick the **format**: NuGet, npm, Maven or Docker); later `/admin/repositories/{name}` (needs a relational database and accounts, like all [repository pages](repositories.md#admin-pages)).

| Setting | Default | Meaning |
|---|---|---|
| Upstream URL | required | A V3 `index.json` URL or a V2 feed URL; for npm the registry's base URL (`https://registry.npmjs.org/`); for Maven the repository root (`https://repo.maven.apache.org/maven2/`); for Docker the registry's address (`https://registry-1.docker.io`). Absolute `http`/`https`, no credentials inside |
| Protocol | V3 | NuGet: `V3` or `V2`. npm, Maven and Docker repositories speak their own protocol only: the form does not ask, and saving always keeps it |
| User name / password | none | HTTP Basic, sent to the upstream's own host only, never to hosts a service index or redirect points to. A token goes in the password; an **npm** upstream with a password and no user name gets it as a bearer token (npm's `_authToken`). Stored protected with ASP.NET Core data protection (the host's key ring; back it up with the host), never shown again, never logged. Blank on save or on *Test connection* keeps the stored one while the URL's scheme/host/port and the user name are unchanged (one rule for both) (on the `mirror` repository only the origin counts: `Mirror:*` never sets a user name, so a changed timeout, `Legacy` or URL path keeps it; otherwise it is dropped and a new one must be typed); *Remove the stored password* clears it. A host without data protection refuses to store a password |
| Metadata cache period | 600 s | How long the upstream's version lists and metadata are reused. `0` asks every time. An expired answer is still served when the upstream cannot be reached |
| Negative cache period | 60 s | How long a `404` from the upstream is remembered. A failure to reach the upstream (network, 5xx, 401) is never remembered as "not found" |
| Download timeout | 600 s | Per HTTP request (V3 only; a V2 upstream is contacted by NuGet's own HTTP stack, with its default timeout) |
| Store artifacts locally | on | On: packages are stored (database and files) on first request; they and their local metadata are served while the upstream is down. Off: **pass-through**: metadata and downloads come from the upstream each time and nothing is kept, so search and the package pages stay empty |
| Who may read it (create form; later *Anonymous read*) | default | *Default*: anonymous requests may read it unless a user name or password is set (then signed-in users with Read only), so a credentialed upstream is never served to anyone by accident; *Anyone* / *Only callers the access model allows* override. The edit page warns when a repository with a stored password is anonymously readable. The auto-created `mirror` repository is anonymously readable like `default`, whose fetches it serves; give it credentials and decide again |
| Offline mode | off | The upstream is never contacted: only stored packages and remembered answers are served |

The cache lives in the repository: cached packages are ordinary package rows of the remote repository (in `repos/{name}/` in storage), isolated from every other repository, and browsable through the repository-aware pages (`/r/{name}/`, `/r/{name}/packages/{id}`, search).

## What a client sees

| Request | Result |
|---|---|
| Version list, registration | Upstream's versions merged with the stored ones; cached for the metadata period |
| Package, `.nuspec` download | Stored copy, else fetched, stored (indexed like a push, never validated) and served; pass-through streams it |
| Push, symbol push (`PUT`) | `403`: the access model never grants *Deploy* on a remote repository, not even to administrators; the browser upload page does not list it |
| Delete, unlist, deprecate | Allowed per the access model (evict a cached package) |
| Read | As for any repository: [access control](access-control.md) |
| Unknown package | `404`, remembered for the negative period |

## Test connection, cache and purge

On `/admin/repositories/{name}`:

| Control | Does |
|---|---|
| *Test connection* | Asks the upstream with the values in the form, saved or not (blank password = the stored one, if the URL's origin and user name are unchanged), and saves nothing. V3: the URL must answer a service index (at most 1 MB is read); V2: any success; npm: `{url}/-/ping`, and `{url}` itself where there is no ping (any success); Maven: the repository root; Docker: `GET /v2/` through the token flow. Redirects are followed only within the same origin (at most 5), never to another host. Shows the HTTP status and time; failures say which (refused credentials, HTTP status, unreachable) without URLs or credentials |
| Cache table | Stored package versions and ids; remembered answers; hits, upstream requests, `404`s answered from memory, expired answers served; the upstream's health (last answer, failing since, last error). Counters and remembered answers are per node and reset at restart |
| *Purge cache* | Deletes every stored package (database and files) and drops the remembered answers. Audited |
| *Purge package* | The same for one package id (npm: package name, `@scope/name`; Maven: `group:artifact`, any version text such as `5.3.20.RELEASE`), one version or all of them. Audited |
| *Browse the cached packages* | The repository's browse page (NuGet: front page; npm: `/npm-packages`; Maven: `/maven-artifacts`; Docker: `/docker-images`) |

Creating a remote repository, saving its upstream, *Test connection* and both purges **ask for your password again** (or a recent sign-in with the external provider, for an account without a password): a test saves nothing but makes the server send a request to a typed URL. The stored password is sent by *Test connection* only while the URL's origin and user name are unchanged (the rule saving applies), so testing another host never hands it to that server. A remote repository with cached packages cannot be deleted until its cache is purged. Creating, editing and purging are refused with `503` while the server is read-only for maintenance (for example during a [database copy](configuration.md)).

## npm remote repositories

An npm repository of kind remote proxies an upstream registry: `npm install` against `/r/{name}/npm/` behaves as against the upstream, but every tarball is downloaded through (and kept by) this server. Same settings as above (URL, credentials, metadata and negative periods, offline, *store locally*, timeout, `Remote:BlockPrivateAddresses`), same pages and password rules.

| Request | Result |
|---|---|
| Packument (`GET /{name}`, scoped `@scope%2fname` or `@scope/name`) | The upstream's document (install-only form when the client asks for it), cached for the metadata period, with **every `dist.tarball` rewritten** to this repository. `404` from the upstream is remembered for the negative period. A version that is not a semantic version is dropped (this server could not serve its tarball name) |
| Upstream down, refusing the credentials or sending something else | An expired remembered answer is served; else what this repository stored (the downloaded versions only, `latest` = the highest of them); else `502 Bad Gateway` with a message without URL or credentials (npm retries it; a `404` would be believed). Never remembered as "not found" |
| Tarball | A stored version is served from storage (also while the upstream is down). Otherwise it is downloaded from the upstream (the packument's own URL when it is on the upstream's origin, else `{url}/{name}/-/{file}`; credentials go to that origin only), hashed while it is written to a temporary file, **checked against the `integrity` / `shasum` the upstream announced** (a mismatch is neither stored nor served: `502`), then stored with its package and version rows and tags and served. Concurrent requests for one version download it once. *Store locally* off: the download is streamed through and nothing is kept |
| Offline mode | The upstream is never contacted: remembered answers and stored versions only; anything else is `404` |
| `npm publish`, dist-tag, deprecate, unpublish | `403` (`401` without credentials): a remote repository takes no changes |
| `npm search` | Searches the **stored** packages only; a registry's catalogue is not searched through a cache |
| `npm audit` | "No advisories", as for every repository (the findings of [vulnerability scanning](vulnerability-scanning.md) are on the package pages, not in the audit endpoint) |

What is kept (a copy cached earlier under another spelling of the version, by case or build metadata, is replaced by the upstream's spelling on the next request for it): a stored version is an ordinary npm version row (manifest, checksums, deprecation, upstream publish time) and its tarball under `repos/{name}/npm/`; the packument itself is **remembered in memory only**, per node (at most 1000 answers per repository, 128 MB each), so after a restart with the upstream down the packument lists only the stored versions. The cache table counts stored versions and packages. Each tarball fetch emits the `mirror.fetch` span (package, version, result; never a URL or credential), as a NuGet mirror fetch does. A tarball is cached under the upstream's exact version spelling only: a file name with other build metadata is a `404` (it used to cache a second copy that the real URL could not reach).

## Maven remote repositories

A Maven repository of kind remote proxies an upstream Maven repository, with the settings above (HTTP Basic credentials, metadata and negative periods, offline, *store locally*; the V3/V2 choice does not exist). *Test connection* asks the repository root: `2xx` is connected; `404` is reported as connected with a note (many repositories list nothing at their root); `401`/`403` is a refusal of the credentials. Behaviour per request (files, checksums verified against the upstream's, metadata, snapshots, offline, `502`) is in [Maven repositories](maven-repositories.md#remote-maven-repositories). The cache table counts stored versions and artifacts; *Purge package* takes `group:artifact` and optionally one version (which also drops the remembered version list).

## Docker remote repositories

A Docker repository of kind remote is a pull-through cache of an upstream registry (`docker pull host/{repo}/{image}`), with the settings above (credentials, metadata and negative periods, offline, *store locally*, `Remote:BlockPrivateAddresses`). Credentials follow the registry's token flow (Basic at the token service the registry names, or a bearer token as a password with no user name); a layer is streamed to the client while it is kept; Docker Hub's official images are asked for under `library/`. *Test connection* does the version check through the token flow; *Purge package* takes an image name and optionally a tag or digest; the cache table counts manifests, images and layer bytes. Behaviour per request is in [Docker registry](docker-registry.md#remote-docker-repositories).

## Upstream addresses (SSRF)

An upstream URL makes **this server** request whatever the administrator typed: loopback, internal services, link-local addresses such as the cloud metadata endpoint `169.254.169.254`. Only administrators can create or edit remote repositories, and internal feeds are a normal use, so private addresses are **allowed by default**. If your administrators are less trusted than the host, set [`Remote:BlockPrivateAddresses`](configuration.md#remote-repositories-refuse-private-upstreams) to `true`: creating, saving and testing then refuse such upstreams, and every V3 connection re-checks the address it actually connects to (so DNS changes and redirects cannot get around it). Whatever the setting, *Test connection* never follows a redirect to another host and reads at most 1 MB (a Docker registry is probed by its own client, which follows a registry's redirects as a pull does, every connection checked against the setting).

## The mirror

`Mirror:Enabled` (see [configuration](configuration.md#enable-read-through-caching)) keeps working unchanged: the default repository fetches from `Mirror:PackageSource`, stores the packages in itself and asks the upstream every time. At startup the **`mirror`** remote repository is created from `Mirror:*` (or its URL, protocol and timeout are brought in line with them) and the default repository fetches **through its settings**, so credentials, an offline switch or cache periods set on `mirror` apply to the default repository too. Its URL, protocol and timeout stay owned by `Mirror:*` (read-only on the page). Packages mirrored into `default` are default packages; `mirror` has its own (initially empty) cache at `/r/mirror/`. If a *local* repository (or one of another format) is already named `mirror`, it is left alone and `Mirror:*` is read directly.

!!! note
    Replace `IRemoteHttpClientFactory` to change how upstreams are contacted (proxy, certificates). Registering your own `HttpClient` no longer affects mirroring. V2 upstreams do not go through the factory: NuGet.Protocol contacts them itself (no proxy/certificate hook, the download timeout does not apply, credentials are given to NuGet's source credential for that host), with NuGet's on-disk HTTP cache switched off so the metadata period and *Purge cache* govern.

## Not covered yet

A remote repository can be a member of a [virtual repository](virtual-repositories.md) (an ordered group behind one URL); `default` with the mirror behaves as one, see [`default` and the mirror](virtual-repositories.md#default-and-the-mirror). Azure Table and `Search:Type` = `AzureSearch` serve the default repository only, so remote repositories cannot be created there. npm, Maven and Docker remotes are tested against in-memory upstreams and a second BaGet (and, when the machine is online, registry.npmjs.org or Maven Central); other registries are covered by the protocol rules above only. Retention policies can limit a remote cache (`CacheLimitBytes`, see [retention](retention.md)).
