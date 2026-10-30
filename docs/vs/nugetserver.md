# NuGet.Server

[NuGet.Server](https://github.com/NuGet/NuGet.Server) is a ASP.NET (.NET Framework) application
that serves a folder of packages over HTTP (OData). **It is deprecated**: the repository was archived on
2026-05-29 and its README states no further features, bug fixes or security patches. Migrate; see the
[migration guide](../import/nugetserver.md).

# BaGet Extension vs NuGet.Server

| Feature | NuGet.Server | BaGet Extension |
| --- | --- | --- |
| Maintenance | Deprecated, archived 2026-05-29 (last release 3.4.3, same day) | Maintained fork |
| Platform | ASP.NET Web Application (.NET Framework 4.6) on IIS | .NET 10; Windows/Linux/macOS, Docker, IIS |
| Protocols | OData V2 only | V3, plus V2 OData at `/api/v2` (on by default) |
| Symbols | No symbol server (`ignoreSymbolsPackages`, default true) | Yes, portable PDBs only |
| Push auth | One shared API key (`apiKey`, `requireApiKey`) | Global key, or accounts with scoped expiring keys (on by default) |
| Accounts / owners | No | Users, organizations, package owners, OpenID Connect, 2FA |
| Delete | Delete, or delist via `enableDelisting` | Unlist (default) or hard delete; relist |
| Mirroring | No | Remote repositories and optional read-through mirror |
| Storage | Folder (`packagesPath`) | FileSystem, Azure Blob, S3, GCS, Aliyun OSS |
| Database | None (folder scan) | SQLite, SQL Server, PostgreSQL, MySQL, Azure Table |
| Search | OData queries (V2 only) | V3 search; package ID substring match |
| Web UI | Feed home page only | Search, details, upload, manage, stats |
| Validation / signing | No | Opt-in validation and repository signing |
| Scale / HA | Not documented | Single node, or [high availability](../high-availability.md) |
| Licence / cost | Apache-2.0, free | MIT, free |

## When to choose which

* New feed, or any feed that must stay patched: BaGet Extension.
* Existing NuGet.Server that must keep running unchanged on IIS: it works, but gets no fixes; plan the move.
* Need V3-only clients, symbols, accounts or cross-platform hosting: BaGet Extension.

## Sources

Checked 2026-10.

* <https://github.com/NuGet/NuGet.Server> (archived, deprecation notice, Apache-2.0)
* <https://github.com/NuGet/NuGet.Server/releases> (3.4.3, 2026-05-29)
* <https://learn.microsoft.com/en-us/nuget/hosting-packages/nuget-server>
* <https://learn.microsoft.com/en-us/nuget/hosting-packages/overview>
