# Nexus

[Sonatype Nexus Repository](https://www.sonatype.com/products/sonatype-nexus-repository) is a multi-format repository manager; NuGet is one of its formats.

| Feature | Nexus Repository | BaGet Extension |
|---|---|---|
| Formats | 27 formats (Maven, npm, Docker, PyPI, NuGet, ...) | NuGet, npm, Maven and Docker repositories (local, remote, virtual); `.nupkg`, `.snupkg`, tarballs, Maven files, OCI images |
| NuGet V3 | Yes | Yes: `/v3/index.json`; registration not paged |
| NuGet V2 | Yes | Yes: `/api/v2` OData, on by default (`Feeds:EnableV2`); `$filter`/`$orderby`/`$select`/`$expand` ignored |
| Symbols | `.snupkg` and Symbol Server protocol on proxy, hosted, group (new in 3.95.0) | Yes: `.snupkg` push, download endpoint; Portable PDB only |
| Proxy / upstream cache | Proxy repositories | [Remote repositories](../remote-repositories.md): per-repository upstream (NuGet, npm, Maven, Docker), credentials, cache periods, offline mode, purge; stored copies are permanent until purged; search does not query upstream |
| Virtual / group feeds | Group repositories (proxy + hosted + groups) behind one URL | [Virtual repositories](../virtual-repositories.md): ordered members (local, remote, nested virtual) behind one URL; uploads go to a chosen member |
| Multiple feeds | Many repositories per instance | Many [repositories](../repositories.md) per instance (`/r/{name}/`) |
| Private (authenticated) reads | Yes: anonymous access can be disabled (default anonymous role is read-only) | Yes: `AnonymousRead` per repository; a private one answers `401` + Basic challenge |
| Permissions model | Roles and privileges per repository; content selectors (path-based) | [Access control](../access-control.md): private repositories, groups, permission targets, project roles; anonymous read per repository; Basic auth or API key for reads; package ownership on top |
| Accounts / API keys | Users, NuGet API keys (NuGet API-Key realm); user tokens Pro only | Accounts (on in shipped config), OpenID Connect, TOTP 2FA, scoped expiring API keys, global `ApiKey` |
| Storage / DB backends | File (all editions); S3 (Community per feature matrix); Azure/GCS Pro only; H2 (default since 3.71) or PostgreSQL | File system, Azure Blob, S3, Google Cloud, Aliyun OSS; SQLite, SQL Server, PostgreSQL, MySQL, Azure Table |
| UI | Web UI (browse, admin, security) | Search, details, upload, stats, package management, account pages |
| Deployment | Self-hosted (Community: self-hosted only); Pro also Cloud | Self-hosted .NET 10 (Windows/Linux/macOS), Docker, IIS |
| HA / scale-out | Pro and Pro+ only (needs external PostgreSQL and shared S3/Azure blob storage) | [High availability](../high-availability.md): several nodes on one database, object store and Redis (not on SQLite, the file system store or Azure Table) |
| Licence / cost | Community free with usage limits; Pro/Pro+ paid | MIT, free; you pay only infrastructure |

Editions (Sonatype tiers page): Community Edition, Pro, Pro+.

| Edition | Limits | Notes |
|---|---|---|
| Community | 40,000 components or 100,000 requests/day; over either limit, no new components until back under both (usage updates hourly) | Free, self-hosted only, no HA; PostgreSQL available |
| Pro | No component limit listed | HA/clustering, SAML/OIDC SSO, cloud blob storage, DR replication; usage-based pricing |
| Pro+ | None listed | Adds malware quarantine, policy gates, licence compliance |

## When to choose which

- Nexus: more formats (PyPI, Conan, ...), vendor support, SSO and malware/policy features in Pro.
- BaGet Extension: NuGet, npm, Maven and Docker with remote and virtual repositories, access control, retention, scanning, replication and HA, with no usage cap and no licence cost.
- Nexus: when you need a format BaGet Extension lacks or vendor support.
- BaGet Extension: when NuGet, npm, Maven and Docker are enough and you want HA without a paid tier.

## Sources

Checked 2026-10.

- <https://help.sonatype.com/en/nuget-repositories.html>
- <https://help.sonatype.com/en/nexus-repository-tiers-and-feature-availability.html>
- <https://help.sonatype.com/en/usage-center.html>
- <https://help.sonatype.com/en/formats.html>
- <https://help.sonatype.com/en/anonymous-access.html>
- <https://help.sonatype.com/en/content-selectors.html>
- <https://help.sonatype.com/en/blob-stores.html>
- <https://help.sonatype.com/en/user-tokens.html>
- BaGet Extension side: this fork's code and docs.
