# Docker registry

A local **Docker** repository is an OCI registry (Distribution Spec v1.1): `docker login`, `push`, `pull`, multi-platform indexes,
referrers (signatures, SBOMs), tag listing, deletes. A **remote** Docker repository caches Docker Hub (or any registry), a **virtual** one merges repositories; see [Remote Docker repositories](#remote-docker-repositories) and [Virtual Docker repositories](#virtual-docker-repositories). Blobs nothing uses are removed by the [garbage collection](#garbage-collection).

## Create and use

| Step | How |
|---|---|
| Create | Administrators: `/admin/repositories` > *Docker* form (name, *Anonymous read*). Needs a store that streams: `Storage:Type` FileSystem, AwsS3, AzureBlobStorage, GoogleCloud or AliyunOss ([streaming storage](formats.md#streaming-storage-b2)); refused otherwise, and on Azure Table / Azure Search (default repository only) |
| Image name | `{host}/{repository}/{image}[:{tag}]`: the BaGet repository is the **first path segment**, the rest is the image (`team/app`, any depth). `docker push localhost:6060/libs/team/app:1.0` pushes image `team/app` into repository `libs`. The registry is at the server root (`/v2/`), not under `/r/{name}/` |
| Login | `docker login host` with your user name and an **API key** as the password (create one on *API keys*). The API key never leaves the login: the registry hands Docker a short-lived token |
| Plain http | Docker only talks http to `localhost` and to registries listed in `daemon.json` `insecure-registries`; the browse page shows the entry for this host |
| Push / pull | `docker tag` + `docker push`, `docker pull`, `docker buildx build --platform linux/amd64,linux/arm64 --push` (snippets on the browse page) |

## Access

pull = Read, push = Deploy, delete = Delete on the image:

| Action | Needs | Notes |
|---|---|---|
| `pull` | **Read** on the repository and the image | anonymous pulls work where *Anonymous read* is on (Docker takes an anonymous token) |
| `push` | **Deploy** | `docker push` also checks which layers exist, so the user needs Read too |
| `delete` | **Delete** | manifests, tags, blobs |

The access model sees the image name (`team/app`) as the package id, so include/exclude patterns (`team/*`) work. A key still needs its
own scope: `package:push` or `package:pushversion` to push, `package:unlist` to delete; its package glob must match the image. There is no
ownership: whoever holds the action may use it. Deploy is never granted on a remote or virtual repository.

### Token flow (`docker login`)

| Request | Answer |
|---|---|
| `GET /v2/` | `200 {}` for a caller the registry knows (token or API key); `401` + `WWW-Authenticate: Bearer realm="https://host/v2/token",service="BaGet"` for an anonymous one. Every response carries `Docker-Distribution-API-Version: registry/2.0` |
| `GET /v2/token?service=BaGet&scope=repository:{repo}/{image}:pull,push` | `{"token","access_token","expires_in","issued_at"}`. Basic credentials are optional: an API key as the password (a user name is checked against the key's owner); wrong ones are `401`. The token grants, per requested image of an existing Docker repository, the requested actions **the caller holds** (possibly none) |
| a registry route without a covering token | `401` + `Bearer … scope="repository:{repo}/{image}:pull,push"`, plus `error="insufficient_scope"` (a token that does not cover it) or `error="invalid_token"` (expired, tampered with). The same answer for a repository that does not exist and one the caller may not see |
| a registry route with an API key (Basic or Bearer) | judged directly, without a token (scripts, `curl`, the conformance suite). A key that may not read is `404`; one that may read but lacks the action is `403` |

Tokens are signed and encrypted with ASP.NET Core data protection, hold only the user name (for the log), the image scopes and the expiry,
never an API key, and last `Oci:TokenLifetime` (5 minutes). A revoked permission applies when the tokens issued before it expire.

## Protocol

| Request | Answer |
|---|---|
| `HEAD`/`GET /v2/{repo}/{image}/blobs/{digest}` | the blob, `Docker-Content-Digest`, `Accept-Ranges`; `Range` (`206`, `416`). Served only through an image that holds it |
| `POST …/blobs/uploads/` | `202` + `Location` (upload session), `Docker-Upload-UUID`, `Range: 0-0` |
| `POST …/blobs/uploads/?digest=` | monolithic upload (`201`) |
| `POST …/blobs/uploads/?mount={digest}&from={repo}/{image}` | `201` when the source image of the same repository holds the blob and the caller may pull it (through a virtual repository: in its deploy repository, so a registry token needs the pull grant for that repository, not only for the virtual name); otherwise a normal `202` session |
| `PATCH …/blobs/uploads/{ref}` | append a chunk (`202`); `Content-Range` start must equal the upload length, else `416` with the real `Range` |
| `PUT …/blobs/uploads/{ref}?digest=` | optional last chunk, SHA-256 verified while streaming, `201`. A wrong digest is `400 DIGEST_INVALID` and ends the session |
| `GET`/`DELETE …/blobs/uploads/{ref}` | `204` with the current `Range`; `204` cancel |
| `PUT …/manifests/{tag or digest}` | `201`; the `Content-Type` must be an OCI or Docker v2 manifest/index type; every config, layer or child manifest must already be in the image (`400 MANIFEST_BLOB_UNKNOWN`), except foreign (non-distributable) layers, i.e. a `foreign`/`nondistributable` media type with `urls`, which are neither required nor stored nor counted; the digest in the URL must match; max `Oci:MaxManifestBytes` (4 MiB, `413`); `OCI-Subject` header when there is a subject |
| `HEAD`/`GET …/manifests/{tag or digest}` | the bytes exactly as pushed, `Docker-Content-Digest` |
| `DELETE …/manifests/{digest}` | removes the manifest and its tags (`202`); `…/manifests/{tag}` removes the tag only |
| `DELETE …/blobs/{digest}` | removes the blob from the image (`202`); `405 UNSUPPORTED` while a manifest of the image uses it |
| `GET …/tags/list?n=&last=` | sorted tags, `Link: rel="next"` when `n` leaves more |
| `GET …/referrers/{digest}?artifactType=` | an OCI index of the manifests whose `subject` is the digest; `OCI-Filters-Applied` |

Errors are the specification's JSON (`{"errors":[{"code","message","detail"}]}`). A path that is not a registry endpoint is `404`, an
undefined method `405`, an invalid name `400 NAME_INVALID`. Repository names match case-insensitively; image names and digests are lower case,
tags are case-sensitive. Only `sha256` digests are stored. Writes are `403` in read-only mode and `503` while a database copy has the maintenance gate closed.

## Remote Docker repositories

A Docker repository of kind remote is a **pull-through cache** of an upstream registry (Docker Hub `https://registry-1.docker.io`, GHCR `https://ghcr.io`, Quay, Harbor, Artifactory, another BaGet, a `registry:2`). Create it on `/admin/repositories` > *New remote repository* (format Docker): upstream address, optional credentials, the cache settings of [remote repositories](remote-repositories.md). `docker pull host/hub/library/nginx:1.27` (BaGet repository `hub`) is answered from the cache, fetched from the upstream on a miss.

| Part | Behaviour |
|---|---|
| Names | The image is the name after the repository, as for a local one (`hub/team/app` is `team/app` upstream). At **Docker Hub** (`registry-1.docker.io`, `index.docker.io`, `docker.io`) a name without a slash is an official image and is asked for as `library/{name}`: `docker pull host/hub/nginx` fetches `library/nginx` |
| Authentication | As `docker login` does: the first request goes without credentials; a `401` with `WWW-Authenticate: Bearer realm="…",service="…",scope="…"` sends BaGet to that **realm** (Docker Hub: `auth.docker.io`) for a token, with the configured user name and password as Basic (anonymously when none: Docker Hub hands out anonymous pull tokens, with its rate limit). The token is kept per scope until it expires and sent as the bearer credential. A registry that asks for `Basic` itself gets the pair at its own host; a password with **no user name** is sent as a ready-made bearer token to the registry. Credentials go to the registry's origin and to the realm only: a **redirect** (Docker Hub sends layers to a CDN) is followed without any credential, never from https to http. A realm on http under an https registry is refused |
| Manifests | A tag is asked of the upstream and cached for the **metadata period**; a digest never changes, so a stored one is never asked again. After the period an unchanged tag is verified with a `HEAD` (which Docker Hub does not count as a pull) and the manifest is not downloaded again. The document must be a supported manifest or index whose digest matches the one asked for (and `Docker-Content-Digest`); with *Store locally* it is kept with its tag (a moved tag moves here, the old manifest stays by digest) |
| Layers | A layer an image holds is served from storage. Otherwise it is **streamed from the upstream while it is written to storage** (a pull is not kept waiting for a multi-gigabyte layer) and kept once its SHA-256 matches the digest: a layer that does not is not kept and the download ends in error. A layer another image of the repository already stores is linked to the image once the upstream confirms (`HEAD`) that the image has it, so a base layer is stored and downloaded once. Range requests, a layer already being downloaded by another request and a read-only server pass through unkept. Concurrency and storage failures never fail a pull |
| Tags | `tags/list` is the upstream's tags (all pages) plus the stored ones, paged here (`n`, `last`) |
| Not found / down | A `404` (or a `401`/`403` after the token service gave a token: Docker Hub answers a missing or private image so) is `404` and is remembered for the **negative period**. A registry that cannot be reached, refuses the credentials at its token service or sends something unusable is `502` (`UNAVAILABLE`), never remembered as "not found"; what was cached before is still served |
| Offline | The upstream is never contacted: stored manifests and layers only |
| Pushes and deletes | `403 DENIED` for anyone who may pull (`docker push` fails with *denied*); the token service never grants `push`/`delete` on a remote repository |
| Referrers, pages, search | Cover what is stored |

*Test connection* does what a client does: `GET /v2/` anonymously, then through the token service with the typed credentials, so a wrong password shows there (it saves nothing and uses a client of its own). *Purge cache* deletes every cached image; *Purge package* takes an image name and optionally one tag or digest. The cache table shows the cached manifests, images and the bytes of their layers. Layers no cached manifest uses are removed by the [garbage collection](#garbage-collection). Docker Hub's anonymous rate limit applies to what BaGet pulls (`GET` of manifests), not to cache hits.

## Virtual Docker repositories

A virtual Docker repository presents several Docker repositories (local or remote, of the same format; another virtual one is flattened in place) as one registry, in the order of its members. Create it on `/admin/repositories` > *New virtual repository* (format Docker), then add members, order them and choose the **deploy repository** (a local member) on `/admin/repositories/{name}`; *Where would an image come from?* takes an image name and a tag or digest and asks each member (*stored here*, *not stored yet; its upstream has it*, *not found*, ...) without fetching or storing anything. Clients use it like any repository: `docker pull host/{virtual}/{image}`.

| Request | Result |
|---|---|
| Manifest by tag or digest | The first member, in order, that has it; a remote member fetches and keeps it. A tag in several members is the first member's |
| Layer, `HEAD` of a layer | The first member that holds it (a remote member in front streams it from its upstream and keeps it; one whose upstream lacks it passes) |
| Tag list | The union of the members' tags, paged here |
| Referrers, browse page, search | Merged across the members (the first member's entry wins an image) |
| Push, upload session, mount, manifest `PUT`, delete | **Moved to the deploy repository** before authorization: the caller needs the action *there*, and the upload URLs the registry hands out stay in the virtual repository. Without a deploy repository: `403 DENIED` |

Only the members the caller may pull the image from are asked (a member the caller cannot read is skipped, never revealed), each in its own scope so its storage, upstream and rules apply as when it is read directly; a member that fails (its upstream is down) is logged and skipped, so the next one answers. **Tokens:** `/v2/token` for a scope on a virtual repository grants `pull` on it, `push`/`delete` as the deploy repository grants them, and `pull` on each member the caller may pull the image from (the token names them, a registry token having no user to ask later). A manage page is not offered (404): use the member's.

## Garbage collection

*Remove unused layers* (Manage) works on one image. The **garbage collection** works on a whole Docker repository, local or remote, and is what keeps a registry from filling up with blobs nobody can reach. Start it on `/admin/operations` (administrators): **Docker garbage collection: dry run** reports and removes nothing, **Docker garbage collection** removes; both are background jobs like the other operations (one run at a time per repository, status kept in memory, audited, refused with `503` while the server is read-only, except the dry run). Before deleting, a run re-checks its plan against the current rows in a serializable transaction: a layer a manifest started using (or a blob another image linked) while the run was going is kept, and the report counts only what was removed. A [retention policy](retention.md) can run it on a schedule after deleting old tags and untagged manifests (*Collect garbage*); the service is `IOciGarbageCollector`.

What it marks, in this order:

| Mark | Meaning | Removed |
|---|---|---|
| Expired upload sessions | idle longer than `Oci:UploadExpiry` | the session row and what it received in storage |
| Unreferenced layers | an image holds a layer none of its manifests uses and it is older than `Oci:PruneGrace`, **unless the image has an upload session still open** (a push sends its layers first and the manifest last, however long they take) | the image's link to the layer |
| Blobs nobody holds | no image holds the blob once those links are gone (reference count 0), or a blob row nobody ever linked, older than `Oci:PruneGrace` | the blob row and its file |
| Orphan files | a file under the repository's `docker/blobs/` that no blob row names (a push that died between storing the file and recording it); only where the store can list files; skipped (the report says so) when a blob of this process was being stored during the listing, and rows are re-read after it, so a push finishing during the run is safe. **Nodes sharing one store are not coordinated**: run it while no other node is receiving pushes | the file |

The report (shown on the Operations page after the run) says how many of each, the bytes freed and the first items; a layer two images hold is removed only when no image holds it, and a manifest, its config and its layers are never touched. A dry run reports exactly what the real run would remove at that moment. A virtual repository has no blobs of its own (the operation is not offered for it); the repository's other formats are not affected.

## Pages

| Page | Address (under `/r/{name}/`) | Who |
|---|---|---|
| Browse | `/docker-images?q=` | whoever may read the repository; lists only images the visitor may read; login, pull, push, buildx and daemon snippets |
| Image | `/docker-images/{image}` | readers of the image: tags, manifests (type, platform, layers, size). `?digest=` shows one manifest: config and layers with sizes, an index's platforms with links to each manifest, annotations, the referrers (signatures, SBOMs; `&artifactType=` narrows them like the API filter), the document |
| Manage | `/docker-manage/{image}` | signed-in users with **Delete** on the image: delete a tag, a manifest (with its tags), remove unused layers (older than `Oci:PruneGrace`; like the garbage collector, not while the image has an open upload session), delete the whole image; each asks the password |

Docker pages are 404 in other formats' repositories and theirs are 404 in a Docker one. In a remote repository the browse and image pages list the cached images, show only the pull commands, and *Manage* is 404. There is no upload page: images are pushed with the CLI (and the NuGet upload page does not offer Docker repositories).

## Storage and data

Blobs are stored once per repository, by digest: `docker/blobs/sha256/{ab}/{digest}` under the repository's prefix, shared by every image of the
repository. Manifests are rows (their bytes are the digest). Tables, migration `AddOciTables` on all four providers:

| Table | Holds |
|---|---|
| `OciRepositories` (repository-scoped) | the images (`team/app`), unique per repository |
| `OciManifests` | digest, media type, artifact type, subject digest, size, the JSON as pushed; unique per image |
| `OciTags` | tag (case-sensitive) to manifest, unique per image; deleted with the manifest |
| `OciBlobs` (repository-scoped) | digest, size, `RefCount` (images holding it); the file goes when it reaches 0 |
| `OciBlobLinks` | which image holds which blob, and how many of its manifests use it |
| `OciUploads` | open upload sessions (reference, storage upload id); idle ones expire after `Oci:UploadExpiry` when the next upload starts |

A blob is served under an image only through its link, so restricting `team/secret` cannot be bypassed by asking another image for its digest.
Deleting a repository needs it empty of images (a remote one: purge its cache first).

## Configuration

| Setting | Default | Meaning |
|---|---|---|
| `Oci:TokenLifetime` | `00:05:00` | validity of a token from `/v2/token` (at least 10 s). Live |
| `Oci:Service` | `BaGet` | the `service` of the challenge, checked at the token endpoint. Live |
| `Oci:MaxManifestBytes` | `4194304` | largest manifest or index. Live |
| `Oci:UploadExpiry` | `1.00:00:00` | idle time after which an upload session is discarded (when the next upload starts, and by the garbage collection). Live |
| `Oci:PruneGrace` | `01:00:00` | a layer no manifest uses is kept this long before *Remove unused layers* or the garbage collection may remove it. Live |

Kestrel has no request body limit; behind IIS a single `PATCH` is capped (`IISServerOptions.MaxRequestBodySize`, `maxAllowedContentLength`): Docker
pushes a layer in one `PATCH`, so raise both ([streaming storage](formats.md#streaming-storage-b2)). Reverse proxies need `client_max_body_size 0` and
must pass `Authorization` and the `Host` header (the token URL is built from it).

## Tests

`OciNamesTests` (Core: names, paths, scopes, manifest reader), `OciUpstreamTests` (Core: remote settings, token challenge, Docker Hub names),
`OciRegistryTests` (HTTP protocol, token flow, access), `DockerPagesTests`, `RemoteOciRepositoryTests` (an in-memory registry with the token flow, a CDN
redirect, private images, corrupt layers and a registry that is down), `VirtualOciRepositoryTests`, `OciGarbageCollectorTests`,
`AdminDockerRemoteVirtualTests` (`BaGet.Tests`), and `DockerRemoteVirtualCliTests` and `DockerCliTests` (`BaGet.Clients.Tests`).
`DockerRemoteVirtualCliTests` pulls through remote repositories whose upstream is a second real BaGet (a private repository that wants the
`docker login` token flow with an API key, and a public one) and the reference `registry:2` image (started by Testcontainers; the test skips
when it cannot be), and through a virtual repository that pushes to its deploy repository; the daemon is told the registries are insecure.
`DockerCliTests`: the real Docker 27 in a privileged `docker:27-dind` logs in, pushes and pulls
multi-layer and 96 MB images, a multi-platform index, public and private repositories, and the `opencontainers/distribution-spec` v1.1.0
conformance suite (push, pull, content discovery, content management) runs against the same server. They skip without a Docker daemon that
allows privileged containers; the images are built `FROM scratch`, so they need no internet (the conformance image comes from `ghcr.io`).

## Vulnerability scanning

Images are scanned by Trivy when `Scanning:TrivyPath` and `Scanning:TrivyRegistryHost` are set; without them images are marked
unscanned in the UI and nothing is blocked. A policy can refuse a manifest pull (`403 DENIED`, tag or digest; layers are
content-addressed and stay fetchable by digest), and a push that reaches the *block deploy* threshold is scanned right after the
push and removed again (`403 DENIED`; a tag the push created or moved, also onto a manifest the image already held, goes back to where it pointed). Trivy's own pull is never refused by *block download* / *block unscanned* (the caller named by `Scanning:TrivyRegistryUser`, or, with no user configured, any pull of the image while its scan runs). See [vulnerability scanning](vulnerability-scanning.md). Tests: `ScanningDockerTests` (a fake
Trivy), `TrivyRunnerTests` (a script standing in for the executable).
