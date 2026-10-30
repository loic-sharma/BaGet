# Local Feeds

[Local feeds](https://learn.microsoft.com/en-us/nuget/hosting-packages/local-feeds), or "folder feeds", use a
folder or network share (e.g. `\\myserver\packages`) as a NuGet source. Add the path with the Package
Manager UI or `nuget sources`.

* Hierarchical layout (NuGet 3.3+): `<id>/<version>/<id>.<version>.nupkg`, created by `nuget add` (one package) or `nuget init` (a folder of packages).
* Older flat layout (single folder) works but is slower.

# BaGet Extension vs local feeds

| Feature | Local feed | BaGet Extension |
| --- | --- | --- |
| Server needed | No (file access only) | Yes (HTTP service) |
| Protocol | File system, no HTTP API | V3, plus V2 at `/api/v2` |
| Search | Client-side: NuGet scans the folder (ID, tags, description); slow on large feeds | Server-side V3 search; Database provider matches package ID only |
| Web UI | No | Search, details, upload, manage, stats |
| Push auth | File-system permissions only | Global key, or accounts with scoped keys |
| Owners / accounts | No | Yes |
| Symbols | Not confirmed | Yes, portable PDBs only |
| Mirroring nuget.org | No | Optional read-through ([remote repositories](../remote-repositories.md)) |
| Storage | Folder or share | Folder, Azure Blob, S3, GCS, Aliyun OSS |
| Remote / internet access | Network share only | HTTP(S) |
| Setup / cost | Minimal | Run a .NET 10 service and a database |

## When to choose which

* One developer, a small team on one LAN, or an offline fallback: a local feed.
* Remote teams, CI over HTTP, authentication, search or symbols: BaGet Extension.
* Moving an existing folder feed into BaGet Extension: see [importing local feeds](../import/local-feeds.md).

## Sources

Checked 2026-10.

* <https://learn.microsoft.com/en-us/nuget/hosting-packages/local-feeds>
* <https://learn.microsoft.com/en-us/nuget/hosting-packages/overview>
