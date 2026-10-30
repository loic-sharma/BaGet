# Package formats

A repository has a **format**. NuGet, npm (see [npm registry](npm-registry.md)), Maven (local, remote and virtual repositories, see [Maven repositories](maven-repositories.md)) and Docker (local, remote and virtual repositories, see [Docker registry](docker-registry.md)) have a protocol today. `/admin/formats` (administrators) lists what this server can host.

| Format | Served under | Feed address | Repository kinds | Storage folders | Status |
|---|---|---|---|---|---|
| NuGet | the repository base (`/v3/…`, `/api/v2`, symbols) | `v3/index.json` | local, remote, virtual | `packages` `symbols` `catalog` `validation` `staging` | done |
| npm | `npm/` | `npm/` | local, remote, virtual | `npm` | done ([details](npm-registry.md)) |
| Maven | `maven/` | `maven/` | local, remote, virtual | `maven` | done ([details](maven-repositories.md)) |
| Docker | `/v2/{repo}/{image}/…` (server root) | `v2/{repo}/` | local, remote, virtual | `docker` | done ([details](docker-registry.md)) |

The *repository base* is `/` for `default` and `/r/{name}/` for the others, so an npm repository `web` is at `/r/web/npm/`. A
route belongs to one format: asking a NuGet route of an npm repository (or an npm route of `default`, which is NuGet) is `404`.
**Docker is the exception** (D2): the OCI Distribution Spec fixes the registry at `/v2/`, so a Docker repository is addressed as `/v2/{repo}/…`
(the BaGet repository is the first path segment of the image name) and its routes carry `RepositoryRouteMetadata.ResolvesRepositoryItself`: the
registry's own filter picks and authorizes the repository, answering in the registry's error format. A `FeedPath` containing `{repo}` is
relative to the server root instead of the repository base.

## Module contract (`IFormatModule`, `BaGet.Core/Formats`)

A module only **describes** its format. Register it with `TryAddEnumerable(ServiceDescriptor.Singleton<IFormatModule, X>())`;
`FormatRegistry` refuses two modules for one format, a shared route prefix, a shared storage folder, or a claim on `repos`.

| Member | Used for |
|---|---|
| `Format`, `DisplayName` | identity |
| `RoutePrefix`, `FeedPath` | where its routes start; the address clients are given (admin pages) |
| `CreatableKinds` | what `IRepositoryService.CreateAsync` accepts (every shipped format: local, remote) |
| `SupportsVirtual` | whether virtual repositories of the format exist (`IVirtualRepositoryAdministration.CreateAsync(..., format)`, created with their members; the format's reads must merge and its writes move to the deploy repository) |
| `StorageFolders` | folders it owns below `RepositoryStorage.Prefix` |
| `CreateFields`, `CreateSettings` | extra inputs of the create form; turned into `Repository.Settings` (`ArgumentException` = message shown) |
| `RepositoryLinks` | links on the Repositories page (NuGet: Browse, Upload, V2 feed, Vulnerabilities, and Catalog/Signatures where offered; npm: Browse, Upload, Registry; Maven: Browse, Upload, Repository; Docker: Browse) |
| `CountArtifactsAsync`, `GetUsageAsync`, `StagePurgeAsync` | package counts; what blocks deleting a repository; rows removed with it |

What a format does per request is a scoped service beside the module, found with `provider.GetFormatService<T>(format)`
(null = not offered):

| Service | Phase | NuGet |
|---|---|---|
| `IFormatIndexer` (deploy) | npm P3 | `NuGetFormatIndexer` over `IPackageIndexingService`; npm has none (publishing needs the caller, so it is `INpmRegistry.PublishAsync`) |
| `IFormatSearchProvider` | npm P3 | `NuGetFormatSearchProvider` over `ISearchService`; `NpmFormatSearchProvider` over `INpmRegistry.SearchAsync` |
| `IFormatRetentionHooks` | P6 | `NuGetRetentionHooks`; npm, Maven and Docker have theirs (`NpmRetentionHooks`, `MavenRetentionHooks`, `DockerRetentionHooks`); used by the [retention engine](retention.md) |
| `IFormatScanner` | P7 | `NuGetFormatScanner` (package and dependency ranges), `NpmFormatScanner`, `MavenFormatScanner` (POM), `DockerFormatScanner` (image manifests, `ScanEngine.Image`): see [vulnerability scanning](vulnerability-scanning.md) |

Routes go through `BaGetEndpointBuilder.MapRoute(..., format: module.Format)` with a pattern starting `"{RoutePrefix}/…"`; it
registers the default and `r/{repo}/` variants and tags the endpoint (`RepositoryRouteMetadata.Format`). Unmarked endpoints are
NuGet's. **Repository pages** (controller actions marked `[RepositoryPage]`) are served under `/r/{repo}/` for every repository and decide per page: `[RepositoryPage(Format = …)]`
names the format of a page (NuGet unless set; the npm pages `/NpmPackages`, `/NpmPackage`, `/NpmUpload`, `/Packages/NpmManage`, the Maven pages `/MavenArtifacts`, `/MavenArtifact`, `/MavenUpload`, `/Packages/MavenManage`, the Docker pages `/DockerImages`, `/DockerImage`, `/Packages/DockerManage`; `/Packages/Scan` serves every format via `AnyFormat = true`),
and `RepositoryActionFilter` answers 404 for a page of another format, except that the NuGet front page of a repository of another format
redirects to the module's `browse` link. The upload picker, header switcher, My packages, API-key repository
limits and the access-model listings now cover every format (the `Format == NuGet` filters are gone). A virtual repository
is of one format and so are its members; a remote repository speaks the format's own upstream protocol (`RemoteProtocol`:
NuGet `V3`/`V2`, `Npm`, `Maven`, `Docker`), set from the format. Azure Table still serves `default` only.

## Data model (B1)

npm tables exist (migration `AddNpmTables` on SQLite, SQL Server, PostgreSQL, MySQL; no pending model change on any):

| Table | Columns | Keys |
|---|---|---|
| `NpmPackages` (repository-scoped) | `Name` (lower case, scope kept, 214), `Created`, `Modified` | unique `(RepositoryKey, Name)` |
| `NpmVersions` | `PackageKey`, `Version` (256), `Integrity` (sha512 SRI), `Shasum` (sha1 hex), `TarballSize`, `Manifest` (JSON, unbounded), `Deprecated`, `Published` | unique `(PackageKey, Version)`; cascade from package |
| `NpmDistTags` | `PackageKey`, `Tag` (128), `Version` | unique `(PackageKey, Tag)`; cascade; no FK to the version |

The packument is built from the manifests, so the registry answers what was published (without the tarball address, built per request).
Tarballs live in storage, not in rows. The npm registry needed no further schema change: its migration is `AddNpmTables`.
Package owners are the common `PackageOwner` rows (repository-scoped, keyed by the lower-case npm name).

Maven (built, migration `AddMavenTables`): `MavenArtifacts` (repo, `GroupId`, `ArtifactId`), `MavenVersions` (version, snapshot flag), `MavenFiles` (name, classifier, extension, size, sha1/sha256/md5/sha512, timestamp+build for snapshot builds); `maven-metadata.xml` is generated from the rows, never stored; GAV stays a string (index length on MySQL).

Docker / OCI (built, migration `AddOciTables`; details in [Docker registry](docker-registry.md#storage-and-data)): `OciRepositories` (image per repository), `OciManifests` (digest, media type, artifact type, `subject` digest for referrers, the JSON as pushed), `OciTags` (case-sensitive, deleted with the manifest), `OciBlobs` (digest unique per repository, size, `RefCount`), `OciBlobLinks` (which image holds which blob; a blob is served only through its link) and `OciUploads` (open sessions). Blobs are files named by digest, stored once per repository; digests are `sha256:` + 64 hex (71, columns 140).

## Client authentication (B3)

| Client | Flow | Verified |
|---|---|---|
| npm | `//host/:_authToken=T` in `.npmrc`; every request after carries `Authorization: Bearer T`. Nothing is sent unprompted without a token; a `401` is final (`E401`, no retry). `npm whoami` is `GET /-/whoami`. `npm login` tries `POST /-/v1/login` (web login); on `404` it falls back to `PUT /-/user/org.couchdb.user:NAME` with `{_id,name,password,type:"user",roles,date}` and expects `{ok,id,rev,token}`; the token is then stored as `_authToken`. The registry therefore needs **bearer tokens** (access tokens) and the CouchDB user call | real npm 10.9.9, `NpmClientFlowTests` |
| Docker | `GET /v2/` answers `401` + `WWW-Authenticate: Bearer realm="…/token",service="…"`; `docker login` then sends `GET {realm}?account=U&client_id=docker&offline_token=true&service=S` with **Basic** (user and password or token), takes `{token,expires_in}` and retries `/v2/` with `Bearer`; pull and push ask `{realm}?scope=repository:NAME:pull,push&service=S` first. Plain HTTP only for `localhost` or an insecure registry. **Built (P5):** `GET /v2/token` validates the Basic credentials (an API key is the password) and answers a signed, short-lived, scope-limited token that never contains the key | real Docker 27 in `docker:27-dind` (`DockerCliTests`: login, push, pull, wrong key refused, private repository) |
| Maven | HTTP Basic from `settings.xml` (`<server><id>` matching the repository id; user, password or token). Maven sends credentials **after a `401`** with `WWW-Authenticate: Basic`; deploy (`PUT`) also challenges first. `401` with a Basic challenge is enough | verified with the real `mvn` 3.9 (`MavenCliTests`): deploy and resolve both send the `settings.xml` credentials after the challenge; Maven 3.8.1+ also blocks plain-http repositories |

## Client test rigs (B6)

`tests/BaGet.Clients.Tests`: `ClientContainers.NodeAsync()` starts `node:22-alpine` once (idle, `sleep infinity`) and runs scripts in
it with an empty `$HOME`. `HostRegistry` is a real Kestrel server on `0.0.0.0` that records requests; the container reaches it as
`host.docker.internal:{port}` (an extra host `host-gateway`, so Linux works too). `WebApplicationFactory` has no socket, so the CLI tests host the real application on such a server for the CLI tests. The tests skip without a Docker daemon (as `BaGet.Database.Tests`).

`docker:dind`: **used by `DockerCliTests`** (`DockerDaemon`): `docker:27-dind` with `--privileged`, `DOCKER_TLS_CERTDIR=` and
`--insecure-registry=<registry host:port>` reaches `docker info` in ~25 s; the `docker` CLI runs inside the same container against the unix socket,
each script with its own `DOCKER_CONFIG`. Images are built `FROM scratch` (no internet). A CI runner must allow privileged
containers; the tests skip when it does not. Maven: `ClientContainers.MavenAsync()` (`maven:3.9-eclipse-temurin-21`) needs no privilege and, like npm, runs `mvn` in a fresh `$HOME`; the plugins come from Maven Central.

## Streaming storage (B2)

`IStreamingStorageService : IStorageService` (`BaGet.Core/Storage`) is the capability a format with huge blobs (Docker layers,
many GB) needs; `PutAsync` takes a whole stream; S3, GCS and Aliyun spool it to a temporary file (not memory) before uploading, and S3/GCS/Aliyun/Azure `GetAsync` of a missing object throws `FileNotFoundException`, like the file store. Nothing is added to `IStorageService`, so
existing providers and tests are unaffected.

| Call | Meaning |
|---|---|
| `BeginUploadAsync(contentType)` | opaque, URL-safe upload id |
| `AppendAsync(id, offset, chunk)` | adds bytes at the length the store holds; another offset throws `StorageUploadOffsetException` carrying the real length (OCI `416` + `Range`). Returns the new length |
| `GetUploadLengthAsync(id)` | where to resume; null = no such upload |
| `CommitAsync(id, path, sha256?)` | makes the content visible at `path` (known only now, a digest path) and removes the upload. `Success`, `AlreadyExists` (identical content) or `Conflict`; a wrong digest throws `StorageDigestMismatchException` and stores nothing |
| `AbortAsync(id)` | discards; unknown ids ignored |
| `GetInfoAsync(path)` / `GetRangeAsync(path, offset, length?)` | `HEAD` and `GET` with `Range`; stream, never a whole blob |

- **Memory:** one part/block buffer per request (8 MiB Azure and GCS, 16 MiB S3 and Aliyun), never the blob. SHA-256 is computed while the bytes stream
  (`UploadDigestTracker`); a node that lost the running hash (restart, another node took the chunk) recomputes it from the stored
  content at commit, still streaming. The caller owns an upload's lifetime (sessions expire; abort).
- **Reads:** S3, Aliyun and GCS serve a range as bounded 64 MiB requests (`WindowedReadStream`), each re-requested from the exact byte
  if its connection drops, so a many-GB `GET` is never one connection that must survive; Azure's read stream already works in windows.
- **Refusal:** `IFormatModule.RequiresStreamingStorage` (default false; the Docker module sets it) makes `RepositoryService.CreateAsync` throw
  `NotSupportedException` (`StreamingStorage.Require`) for a local or remote repository of that format when the configured store is not
  an `IStreamingStorageService`; virtual repositories hold no blobs and are exempt. Every shipped provider streams, so today this guards custom stores.

| Provider | Mechanism | Verified against |
|---|---|---|
| FileSystem, Null | file grown under `.uploads/`, moved into place on commit | in-process |
| AwsS3 | multipart into `.uploads/`, bytes short of a part kept in a `.tail` object, copied to the final key by ranged multipart copy completed with `If-None-Match: *` | S3Mock (Testcontainers) |
| AzureBlobStorage | staged blocks of a temp blob (`.uploads/`), committed then copied server side if-not-exists | Azurite |
| GoogleCloud | resumable session over HTTP (8 MiB chunks, a multiple of 256 KiB), tail object, copy with `ifGenerationMatch=0` | `oittaa/gcp-storage-emulator` |
| AliyunOss | as S3 (multipart + tail + ranged copy) | **no emulator exists**: code review and the shared contract only; commit of the same key is not atomic (the SDK sends no `x-oss-forbid-overwrite`) |

Measured (5 GiB blob in 256 MiB chunks, growth of the test process's private memory; B2 of the plan): FileSystem 6 MiB, Azure/Azurite 55 MiB,
S3/S3Mock 68 MiB; GCS 1 GiB 11 MiB (the emulator keeps blobs in RAM, a 5 GiB run did not finish); Aliyun not measurable. Azure
block ids are ordered by their index, not their base64 text (the 5 GiB run caught a scrambled blob).

Emulator limits (not service limits): the GCS emulator ignores `DELETE` on a session and `ifGenerationMatch` on a copy, so those
contract assertions are switched off for it (`CancelledSessionsAreGone`, `ConditionalCopyIsEnforced`); MinIO was planned for S3 but no longer
publishes images. Tests: `tests/BaGet.Storage.Tests` (one shared contract run against every provider; skips without Docker). The
large-blob test streams 96 MiB by default and 5 GB with `BAGET_STORAGE_LARGE_BYTES=5368709120`, measuring the process's private memory.

**Body limits for chunked uploads.** The host sets Kestrel's `MaxRequestBodySize` to unlimited (`Program.cs`; the plan's "~2 GB" was
`FormOptions.MultipartBodyLengthLimit`, which only form posts see, not a raw `PATCH`). Under IIS the limits are per request, so a
client that sends a layer in one `PATCH` hits them: `IISServerOptions.MaxRequestBodySize` (BaGet default 250 MB) and `web.config`
`requestLimits maxAllowedContentLength` (30 MB default, 4 GB ceiling). Raise both, or put the registry behind a proxy with no body
limit; a client that chunks (`Content-Range`) stays under them. See [Configuration](configuration.md#iis-server-options).
