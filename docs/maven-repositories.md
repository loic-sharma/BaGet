# Maven repositories

A local **Maven** repository is a Maven 2 repository: `mvn deploy`, dependency resolution, `maven-metadata.xml`, checksums, SNAPSHOT
builds, a release/snapshot policy. Gradle's `maven-publish` and `maven { url }` use the same protocol. A [remote](#remote-maven-repositories) Maven repository
proxies Maven Central (or any Maven repository) and a [virtual](#virtual-maven-repositories) one merges several.

## Create and use

| Step | How |
|---|---|
| Create | Administrators: `/admin/repositories` > *Maven* form (name, versions accepted, redeploy, snapshot builds kept; *Anonymous read*). Refused on Azure Table / Azure Search (default repository only) |
| Address | `https://host/r/{name}/maven/` (shown on the repository list and the browse page). `/maven/` of the root is never served: `default` is NuGet |
| Credentials | HTTP Basic: Maven sends the `settings.xml` `<server>` after a `401` challenge. Password = a user API key (user name = its owner) or the global `ApiKey` (any user name) |
| Deploy | `mvn deploy` with `<distributionManagement>` (snippets on the browse and upload pages), or `mvn deploy:deploy-file` |
| Resolve | `<repositories>` entry with the same `<id>` as the `<server>`; `mvn dependency:copy-dependencies`, `dependency:get`, Gradle |

Maven 3.8.1+ **blocks plain-http repositories** (a built-in mirror `maven-default-http-blocker`): serve BaGet over https, or override that mirror
in `settings.xml`.

## Protocol

| Request | Answer |
|---|---|
| `GET`/`HEAD` file | the stored file, `X-Checksum-Sha1/Md5/Sha256` headers |
| `GET` `…/{file}.sha1` `.md5` `.sha256` `.sha512` | computed once on the way in, kept in the file's row |
| `GET` `maven-metadata.xml` (group, artifact, version level) | generated from the rows, never stored; SNAPSHOT versions list the timestamped builds (`timestamp`, `buildNumber`, `snapshotVersions`) |
| `PUT` file | stored; checksum and metadata uploads are acknowledged (they are generated); `201`. A snapshot build is stored under its timestamped name and the build number advances |
| `DELETE` file | one file with its checksums (`204`); not Maven's protocol, but what scripts use |
| Refused | `409` policy refuses the version, or a release file already exists (unless *Redeploy* is on and the caller holds Delete); `400` name outside the layout; `413` over `Maven:MaxFileBytes` (1 GiB); `403` read-only mode |

A `.pom` deployed to a version is parsed (name, description, URL, licences, packaging, dependencies) for the pages and search; a POM that
does not parse still stores. Layout rules (`MavenNames`): ids use Maven's characters only, so a path never leaves its folder.

## Policy (per repository, *Edit repository > Maven settings*)

| Setting | Effect |
|---|---|
| Versions accepted | `Releases` (default), `Snapshots` or `Both`. A stricter policy deletes nothing stored |
| Redeploy releases | off: a release file is immutable (409). On: replaced by a caller who also holds Delete. Simultaneous deploys of one file are serialised per process (the loser gets 409 and the winner's file stays); several nodes on one store are not coordinated |
| Snapshot builds kept | 0 keeps all. N keeps the newest N builds of a snapshot version, deleting older ones on each new deploy; the manage page cleans up on demand |

## Pages

| Page | Address (under `/r/{name}/`) | Who |
|---|---|---|
| Browse | `/maven-artifacts?q=` | whoever may read the repository; lists only artifacts the visitor may read (`group:artifact`); `settings.xml` and `pom.xml` snippets |
| Artifact | `/maven-artifacts/{group}/{artifact}/{version?}` | readers of the artifact: versions, files with sizes and checksums, POM view, dependency snippets (Maven, Gradle), `mvn dependency:get` |
| Upload | `/maven-upload` | a signed-in user with Deploy (accounts on), or the API key typed into the form (accounts off): coordinates, packaging, classifier, file |
| Manage | `/maven-manage/{group}/{artifact}` | owners and administrators: owners, delete a file / a version / the artifact (password), snapshot clean-up |

Maven pages are 404 in a NuGet or npm repository and the other formats' pages are 404 in a Maven one. Access patterns (grants, include/exclude)
see the artifact as `group:artifact`. Writes are refused (503) while a database copy has the maintenance gate closed.

## Storage and data

Files: `maven/{group}/{artifact}/{version}/{file}` under the repository's prefix (`repos/{name}/` as for every non-default repository).
Tables `MavenArtifacts`, `MavenVersions`, `MavenFiles` (repository-scoped; migration `AddMavenTables`, all four providers). Owners are the
common `PackageOwner` rows keyed by `group:artifact`.

## Remote Maven repositories

A remote Maven repository caches an upstream: Maven Central (`https://repo.maven.apache.org/maven2/`), another BaGet, Artifactory, Nexus. Create it at `/admin/repositories` > *New remote repository* > format *Maven*; the settings (URL, credentials, cache periods, offline, *store locally*, download timeout), *Test connection*, the cache table and both purges are those of [remote repositories](remote-repositories.md#maven-remote-repositories). Point `settings.xml` at it with a `<mirror>` of `central` (or of `*`, which also sends plugins through it); the browse page shows the snippet.

| Request | Result |
|---|---|
| File (`GET`) | Stored: served from storage with its checksums (also while the upstream is down). Else downloaded, hashed while written to a temporary file, compared with the checksum the upstream publishes (`.sha1`, else `.sha256`, else `.md5`; a mismatch is neither stored nor served: `502`), stored, served. An `HTML` page answered `200` for a missing file is a `404` |
| `HEAD` | Stored: as for a local repository. Else the upstream is asked for the headers only; nothing is downloaded or kept |
| Checksum of a file not stored yet | The upstream's, remembered like metadata; the file is not downloaded for it |
| `maven-metadata.xml` (artifact, version, group level: plugin prefixes resolve too; a group's plugin list is judged by the repository's Read, not by an artifact named like the group's last folder) and its checksums | The upstream's, cached for the metadata period (`404` for the negative period; an expired one while the upstream is down). Else generated from the versions this repository stored; else `502` (down) or `404` |
| `-SNAPSHOT` | A timestamped build is immutable and kept. The moving name (`lib-1.0-SNAPSHOT.jar`) is always streamed from the upstream and never kept; a stored build answers it only while the upstream cannot. Use a metadata period of `0` to see a new build at once |
| `PUT`, `DELETE` | `403` (`401` without credentials): a remote repository takes no changes. The release/snapshot policy does not apply: whatever the upstream has is served |
| Pages and search | The **stored** artifacts only (browse, artifact page); no owners or *Manage* |
| Private repository | The upstream is not asked (not even to tell a version's metadata from an artifact's) for a caller who may not read the repository |

Files are ordinary Maven rows and files (`repos/{name}/maven/…`), so counting, search, access, the delete guard and the database copy need nothing new. Metadata and checksum answers are remembered **in memory** per node.

## Virtual Maven repositories

A virtual repository of format Maven merges Maven repositories (`/r/{name}/maven/`). Members are read in their own scope (storage, upstream and access rules), only those the caller may read; a member that fails is skipped.

| Request | Result |
|---|---|
| File and its checksums | The first member that has it, so a file and its checksum always agree |
| Artifact `maven-metadata.xml` | `versions` is the union (Maven's ordering); `latest` and `release` are the first member's own that the merged list contains (**member order**, not the highest version; the highest when no member names one); `lastUpdated` the latest. Its checksums describe the merged document |
| Version `maven-metadata.xml` (snapshot builds) | The first member's that has it (the builds it names are that member's files) |
| Group `maven-metadata.xml` (plugin prefixes) | The union of the members' plugins, a prefix once (first member wins) |
| `PUT`, `DELETE`, upload | **Moved to the deploy repository** before authorization (its rules and the caller's rights there); without one `403` |
| Pages, search | Merged by `group:artifact`; *Manage* is `404` (use the member) |

The *Where would a package come from?* preview takes `group:artifact` and any Maven version (`5.3.20.RELEASE`). See [virtual repositories](virtual-repositories.md#maven-virtual-repositories).

## Tests

`MavenLayoutTests` (Core), `MavenRepositoryTests`, `MavenPagesTests`, `RemoteMavenRepositoryTests`, `VirtualMavenRepositoryTests`, `AdminMavenRemoteVirtualTests` (`BaGet.Tests`, the upstream is the in-memory `FakeMavenUpstream`), and `MavenCliTests` (`BaGet.Clients.Tests`): the real
`mvn` 3.9 in `maven:3.9-eclipse-temurin-21` deploys and resolves releases and snapshots, hits the 409 rules, and signs in to a private
repository with `settings.xml` credentials. They skip without Docker and need the internet (the plugins come from Maven Central).

`MavenRemoteVirtualCliTests` (`BaGet.Clients.Tests`): the real `mvn` against a remote repository whose upstream is a second real BaGet (releases, timestamped snapshots, upstream gone, `502`, deploy refused), a virtual repository, and, with the internet, the real Maven Central through a `*` mirror (plugins and prefix metadata included).

## Vulnerability scanning

With `Scanning:Enabled`, each stored version and the compile and runtime dependencies of its POM that state a literal version are
checked against OSV. A policy can refuse the files of a version (`403`, plain text; `maven-metadata.xml` stays readable) and a
deploy whose artifact or POM dependencies have findings (nothing stored). See [vulnerability scanning](vulnerability-scanning.md).
Tests: `ScanningMavenTests`.
