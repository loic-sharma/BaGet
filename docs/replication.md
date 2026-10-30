# Replication

A replication copies a **local repository** to (push) or from (pull) a repository of **another BaGet server**, in the same
format, through that format's own protocol. Everything is in **Admin > Replication** (`/admin/replication`) and
`/api/v1/replication`; administrators only. Needs a relational database (not Azure Table).

## A replication

| Setting | Meaning |
|---|---|
| Local repository | a **local** repository (remote caches an upstream, virtual stores nothing) |
| Direction | **push**: this repository is the source; **pull**: the peer's repository is the source |
| Peer address, peer repository | the other server's base address (`https://other.example/`) and its repository (`default` or a name); same format, and for a push it must be local |
| Peer API key | an **administrator's key with the `admin:api` scope** on the peer (the inventory is an admin API call; deploy rights follow from being an administrator); required, even for a pull |
| Enabled | a disabled replication never runs (manual runs refused) |
| On deploy | push only: a few seconds after the last package/version/file/tag stored in the local repository, a run starts |
| Repeat every N minutes | schedule, counted from the later of the last run and the moment it was set |
| Delete propagation | delete at the target what **this replication copied** and the source has since deleted; nothing else is ever deleted |
| Conflict | both sides hold the item with different content: **target wins** (kept, logged) or **source wins** (source's copied over the target's; a push deletes the peer's copy first except for Docker, see below) |

The key is stored protected (the remote-repository password mechanism) and sent as a bearer token to the peer's address
only; no page, API answer, log or audit entry shows it. Changing the peer's address drops the stored key unless a new one
is typed. `Remote:BlockPrivateAddresses` applies to peer addresses like to upstreams. Changing the repository, direction or
peer clears the record of what was copied.

## What is copied

| Format | Item | Protocol used | Conflict detection |
|---|---|---|---|
| NuGet | package version | `PUT api/v2/package` (push), flat container `.nupkg` then the normal submit pipeline (pull) | none: same id (any case) and version = same package |
| npm | version, with the dist-tags that point at it | `PUT {name}` with the tarball streamed from a temp file (push), packument + tarball (pull) | tarball `integrity` |
| Maven | one **file** of a version (jar, pom, ...); metadata and checksums are generated | `PUT`/`GET`/`DELETE` of the file path | SHA-256 |
| Docker | a **tag**: manifest/index, child manifests, missing blobs | `HEAD`/`POST ...?digest=`/`PUT manifests` (push), `GET` (pull) | manifest digest |

Layers and tarballs are streamed (spooled to temporary files), never held in memory. A blob the receiver holds is not sent.
Referrers and untagged manifests travel only when a copied index names them.

The peer's API key goes to the peer's own origin only: the connection does not follow redirects by itself, and a `GET`/`HEAD` redirect is followed by BaGet with the key only when the target is the same origin (never from https to http); other redirects fail the item.

A **pull** is subject to the receiving repository's [scanning policy](vulnerability-scanning.md) like a push: a NuGet package or Docker image that reaches the *block deploy* threshold is not kept (a failed item with the reason in the run's detail). npm and Maven pulls are checked by the same rule.

With *source wins*, a pull **replaces** the local copy only after the peer's item is fetched and accepted (integrity, scanning policy): a refused or failed replacement keeps what was there (a Docker tag simply moves, and a refused image puts the previous manifest back). A push moves a Docker tag in place (the manifest `PUT`), so nothing is deleted at the peer first. For NuGet, npm and Maven the peer refuses to overwrite a stored version or release file (`409`), so a push deletes the peer's copy first and a peer that then refuses the item (scanning policy) is left without it until the next run.

## Runs, log, interruption

A run lists both sides (`GET /api/v1/replication/inventory/{repository}` on the peer), plans (copy what the target lacks,
skip what it holds, apply the conflict rule, propagate deletes; an item the target changed after it was copied, by digest, is deleted only under `SourceWins`, else forgotten), executes and writes a **log entry**: status
(`Running`, `Succeeded`, `Failed`, `Interrupted`), counts (examined, copied, skipped, deleted, conflicts, failed), bytes and the
first failures and conflicts. One item failing is logged and the run goes on; a **peer that cannot be reached**
ends the run as `Failed` (not one failure per item). Stopping a run, stopping the server, or a read-only server ends it as
`Interrupted`; a process that died leaves a `Running` entry that the worker marks `Interrupted` (when no node holds its lease).

**Resume** is simply another run: it plans from what both sides hold now, so what was copied is skipped and the rest is copied
(the new entry names the one it continues). Each copied item is recorded as it goes, so progress survives any interruption.

One run per replication at a time (`IJobLease`, name `replication:{key}`; the default holds it in this process only, the
high-availability phase replaces it with a cluster-wide lock). Runs and the worker (every 15 s) are idle while the server
is read-only.

## Pages and API

| Page / endpoint | Does |
|---|---|
| `/admin/replication` | list, create (password) |
| `/admin/replication/{id}` | settings (save with password), **Test connection** (password, saves nothing), **Run now** / **Resume** (password), **Stop**, the log, delete (password) |
| `GET/POST /api/v1/replication/configs`, `GET/PUT/DELETE .../{key}` | list, create, show, replace, delete |
| `POST .../configs/{key}/run`, `.../resume`, `.../stop`, `POST .../test` | start, resume, stop, test |
| `GET .../configs/{key}/runs`, `GET .../runs/{key}` | the log |
| `GET .../inventory/{repository}` | what a repository holds (`summary=true`: the count); what a peer calls |

See [Administration REST API](admin-api.md) for keys and confirmation, and [UI coverage](ui-coverage.md).
