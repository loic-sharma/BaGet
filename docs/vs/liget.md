# LiGet

[LiGet](https://github.com/ai-traders/liget) is a Linux-first, Docker-oriented NuGet server. Since 1.0 it is
itself a fork of upstream BaGet (per its README). It is **dormant**: not archived, but the last release is
1.0.3 (2019-04-29) and the last commit is 2021-01-06.

# BaGet Extension vs LiGet

| Feature | LiGet | BaGet Extension |
| --- | --- | --- |
| Maintenance | Dormant (last commit 2021-01) | Maintained fork |
| Runtime | Kestrel on .NET Core 2.0 (README) | .NET 10 |
| Protocols | V3 for private packages and V3 search; limited V2 (`FindPackagesById()`, `Packages()`, `PUT /api/v2`; no V2 search/filter) | V3, plus V2 OData at `/api/v2` (`searchTerm`, `id`, `$skip`/`$top`) |
| Paket | Tested with Paket; older Paket versions unsupported | Not specifically tested; not confirmed |
| Metadata | SQLite via EF (only listed DB) | SQLite, SQL Server, PostgreSQL, MySQL, Azure Table |
| Package storage | `simple2` file backend only | FileSystem, Azure Blob, S3, GCS, Aliyun OSS |
| Push auth | One API key (SHA256 hash in `LIGET_API_KEY_HASH`) | Global key, or accounts with scoped keys (on by default) |
| Accounts / owners | Not mentioned | Users, organizations, owners, OpenID Connect, 2FA |
| Upstream | Read-through cache of nuget.org packages on a separate endpoint (`/api/cache/v3/index.json`); no search for cached packages | Read-through into the same feed, merged version lists; search sees local/pulled packages only |
| Symbols | Not mentioned | Yes, portable PDBs only |
| Web UI | Not confirmed | Search, details, upload, manage, stats |
| Validation / signing | Not mentioned | Opt-in |
| Deployment | Docker image (`tomzo/liget`) | Docker, IIS, any host with .NET 10 |
| Licence | MIT | MIT |

## When to choose which

* Any new deployment: BaGet Extension (current runtime, active development).
* Existing LiGet with Paket workflows you cannot change: keep it, test Paket against BaGet Extension first.
* Need accounts, ownership, symbols, validation or non-SQLite databases: BaGet Extension.

## Sources

Checked 2026-10.

* <https://github.com/ai-traders/liget> (README, MIT)
* <https://github.com/ai-traders/liget/releases> (1.0.3, 2019-04-29)
* <https://learn.microsoft.com/en-us/nuget/hosting-packages/overview> (describes LiGet as "an open-source implementation of NuGet V2 server"; the README says V3 plus limited V2)
