# nuget.org

[nuget.org](https://www.nuget.org/), the "Gallery", is the default public NuGet feed. Publish open-source
packages there; use BaGet Extension for private or mirrored feeds.

Gallery code (self-hosting is possible but not simple; the
[hosting wiki](https://github.com/NuGet/NuGetGallery/wiki/Hosting-nuget.org's-v3-services) was last edited in 2018, so verify it before use):

* [NuGet/NuGetGallery](https://github.com/NuGet/NuGetGallery) - website, jobs, V3 pipeline and search
* NuGet.Jobs, NuGet.Services.Metadata and ServerCommon are archived and merged into NuGetGallery

# BaGet Extension vs nuget.org

| Feature | nuget.org | BaGet Extension |
| --- | --- | --- |
| Visibility | Public; no private packages | Public or internal; private repositories with Basic auth or API key reads ([access control](../access-control.md)) |
| V3 (push, restore, search, autocomplete, registration) | Yes | Yes (registration not paged) |
| V2 (OData) | Yes | Yes, on by default (`Feeds:EnableV2`); `$skip`/`$top`, `searchTerm`, `id`, `includePrerelease`, `semVerLevel`; `$filter`/`$orderby`/`$select`/`$expand` ignored |
| Symbols (`.snupkg`) | Yes | Yes, portable PDBs only |
| Catalog | Yes | Yes, relational DB only (`catalog rebuild` to backfill) |
| Vulnerability info | Yes | Manual entry through the [API](../advanced/api.md), plus [scanning](../vulnerability-scanning.md) against OSV.dev (online or offline copy), off by default |
| Deprecation | Yes | Manual (API or manage page) |
| Accounts, organizations, owners | Yes (Microsoft/Entra login) | Yes, on by default (`Accounts:Enabled=true` in shipped settings); optional OpenID Connect |
| API keys | Yes: scoped (glob), expiring; new keys max 30 days since 2026-08-17, older keys expire 2026-11-01 | Global `ApiKey`, plus scoped expiring per-account keys (on with accounts) |
| Two-factor sign-in | Required since 2022-03-08 (via MSA/Entra) | TOTP + recovery codes |
| ID prefix reservation | Yes | Not supported (per-ID verified-owner flag only, no UI) |
| Validation before serving | Yes | Opt-in (`Validation:Enabled`), in-process; no malware scanner built in |
| Repository signing | Yes | Opt-in (needs a certificate) |
| Package size limit | 250 MB | About 2 GB for push and upload (multipart limit); IIS 250 MB |
| Deletion | Generally not supported ([policy](https://learn.microsoft.com/en-us/nuget/nuget-org/policies/deleting-packages)) | Unlist (default) or hard delete; overwrite off by default |
| Mirroring | n/a | [Remote repositories](../remote-repositories.md) (per repository) and the optional `Mirror:Enabled` mirror; search does not query upstream |
| Download statistics | Not re-verified | Totals plus `/stats` page; no per-day |
| Search | Full index | Package ID substring only (`Database`), or Azure Search |
| Administration | Admin-only Settings page (edits settings, marks restart-required ones); copy of all data between SQLite, SQL Server, PostgreSQL and MySQL behind a read-only gate; optional SMTP email |
| Web UI | Full Gallery | Search, details, upload, manage, stats, account |
| Storage / DB | Azure (Blob, SQL, Search, CDN) | FileSystem, Azure Blob, S3, GCS, Aliyun OSS; SQLite, SQL Server, PostgreSQL, MySQL, Azure Table |
| Platform | Azure-hosted service | .NET 10, Windows/Linux/macOS, Docker, IIS |
| Scale / HA | Global, CDN-backed | Single node, or several with [high availability](../high-availability.md) (database, object store and Redis shared) |
| Licence / cost | Free service; Gallery code Apache-2.0 | MIT, self-hosted, infrastructure cost only |

## Architecture

* nuget.org V3 is static (JSON on blob storage behind a CDN, built by catalog jobs): reads scale
  almost without limit, writes and operation are heavy and tied to Azure.
* BaGet Extension V3 is dynamic (service plus database): simple to deploy, easy to extend, weaker read
  scaling and reliability.

## When to choose which

* Publish public open-source packages: nuget.org.
* Internal or private packages, or a cache of nuget.org for CI or offline use: BaGet Extension.
* Need CDN-scale reads or prefix reservation: nuget.org.
* Need your own accounts, ownership and validation on infrastructure you control: BaGet Extension.

See also [importing from nuget.org](../import/nugetorg.md) and [configuration](../configuration.md).

## Sources

Checked 2026-10.

* <https://learn.microsoft.com/en-us/nuget/nuget-org/nuget-org-faq>
* <https://learn.microsoft.com/en-us/nuget/api/overview>
* <https://learn.microsoft.com/en-us/nuget/hosting-packages/overview>
* <https://github.com/NuGet/NuGetGallery> (Apache-2.0, active)
* <https://github.com/NuGet/NuGetGallery/wiki/Hosting-nuget.org's-v3-services>
* <https://devblogs.microsoft.com/dotnet/?p=60516> (API key expiry)
* <https://devblogs.microsoft.com/nuget/requiring-two-factor-authentication-on-nuget-org/>
