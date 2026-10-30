# Artifactory

[JFrog Artifactory](https://jfrog.com/artifactory/) is a commercial, multi-format artifact repository manager; NuGet is one of its package types.

| Feature | Artifactory | BaGet Extension |
|---|---|---|
| Formats | Multi-format (Maven, npm, Docker, PyPI, NuGet, ...) | NuGet, npm, Maven and Docker repositories (local, remote, virtual); `.nupkg`, `.snupkg`, tarballs, Maven files, OCI images |
| NuGet V3 | Yes: `/api/nuget/v3/<repo>/index.json`; PackageBaseAddress only on repos with *Enforce Layout* (fixed at creation) | Yes: `/v3/index.json`; registration not paged |
| NuGet V2 | Yes: `/api/nuget/<repo>` | Yes: `/api/v2` OData, on by default (`Feeds:EnableV2`); `$filter`/`$orderby`/`$select`/`$expand` ignored |
| Symbols | Local, remote and virtual symbol repos; `.snupkg`; Microsoft PDB v7 and Portable PDB | Yes: `.snupkg` push, download endpoint; Portable PDB only |
| Proxy / upstream cache | Remote repos (nuget.org or another Artifactory); cached on demand | [Remote repositories](../remote-repositories.md): per-repository upstream (NuGet V3 or V2, npm, Maven, Docker), credentials, metadata and negative cache periods, offline mode, pass-through or stored copies, test connection, purge; plus the optional `Mirror:Enabled` mirror for the default repository. Stored copies are permanent until purged; search does not query upstream |
| Virtual / group feeds | Virtual repos combine local and remote behind one URL; push allowed | [Virtual repositories](../virtual-repositories.md): ordered members (local, remote, nested virtual), first match answers, versions and search merged, uploads to a default deploy repository, only readable members used; no merged catalog, no per-member path rules |
| Multiple feeds | Many repositories per instance | Many [repositories](../repositories.md) per instance (`/r/{name}/`), each with its own format and access |
| Private (authenticated) reads | Yes: anonymous access configurable (JFrog recommends off; anonymous removed from default permissions since 7.84.3); *Force Authentication* per repo | Yes: `AnonymousRead` per repository; a private one answers `401` + Basic challenge (`404` without Read) |
| Permissions model | Permission targets (repos + users/groups); Permissions V2 from 7.77.2; project RBAC | [Access control](../access-control.md): private repositories, groups, permission targets, project roles; anonymous read per repository; Basic auth or API key for reads; package ownership on top |
| Accounts / API keys | Users, groups, access tokens; API key as `user:key` in NuGet clients | Accounts (on in shipped config), OpenID Connect, TOTP 2FA, scoped expiring API keys, global `ApiKey` |
| Storage / DB backends | Filestore: file system, S3, Azure Blob, GCS, NFS; DB: PostgreSQL (default since 7.84.7), MySQL, MariaDB, MSSQL, Oracle, Derby (not for production) | File system, Azure Blob, S3, Google Cloud, Aliyun OSS; SQLite, SQL Server, PostgreSQL, MySQL, Azure Table |
| UI | Admin and browse UI | Search, details, upload, stats, package management, account pages |
| Deployment | JFrog-hosted SaaS or self-managed | Self-hosted .NET 10 (Windows/Linux/macOS), Docker, IIS |
| HA / scale-out | Self-managed HA listed under Enterprise X; SaaS Enterprise X 99.9% uptime | [High availability](../high-availability.md): several nodes on one database, object store and Redis (not on SQLite, the file system store or Azure Table) |
| Licence / cost | Commercial, see below | MIT, free; you pay only infrastructure |

Artifactory editions and pricing (pricing page):

| Plan | SaaS | Self-managed |
|---|---|---|
| Pro (SaaS) / Pro X (self-managed) | SaaS "Pro" $150/mo ($50 promo, monthly only); 25 GB base consumption, per-GB overage | Self-managed "Pro X" $27,000/yr, 1 server |
| Enterprise X | from $950/mo; 125 GB base | from $51,000/yr, 3 servers; HA |
| Enterprise + | custom | custom |

- SaaS is priced by storage and transfer consumption; self-managed by server count and project count, minimum one-year term.
- Free offerings per JFrog: Artifactory OSS (Java only), Community Edition for C/C++ (Conan and generic only), cloud trial. No free edition with NuGet was found.

## When to choose which

- Artifactory: more formats (PyPI, Conan, ...), vendor-backed HA and support, JFrog Xray, a mature ecosystem.
- BaGet Extension: NuGet, npm, Maven and Docker with local, remote and virtual repositories, access control, retention, [vulnerability scanning](../vulnerability-scanning.md) (OSV, Trivy), [replication](../replication.md) and HA, with no licence cost and a simple self-hosted deployment.
- Artifactory: when you need a format BaGet Extension lacks or a vendor SLA.
- BaGet Extension: when NuGet, npm, Maven and Docker cover the need and a free, MIT-licensed server is wanted.

## Sources

Checked 2026-10.

- <https://docs.jfrog.com/artifactory/docs/nuget-repositories>
- <https://jfrog.com/pricing/>
- <https://jfrog.com/community/open-source/>
- <https://docs.jfrog.com/administration/docs/permissions>
- <https://docs.jfrog.com/administration/docs/access-tokens>
- <https://docs.jfrog.com/installation/docs/artifactory-storage>
- <https://docs.jfrog.com/installation/docs/database-configuration>
- <https://docs.conan.io/2/tutorial/conan_repositories/setting_up_conan_remotes/artifactory/artifactory_ce_cpp.html>
- BaGet Extension side: this fork's code and docs.
