# Azure Artifacts

[Azure Artifacts](https://learn.microsoft.com/en-us/azure/devops/artifacts/start-using-azure-artifacts) is the package management service of Azure DevOps (cloud service and Azure DevOps Server).

| Feature | Azure Artifacts | BaGet Extension |
|---|---|---|
| Package formats | NuGet, npm, Maven (incl. Gradle), Python, Cargo, Universal Packages | NuGet, npm, Maven and Docker repositories (local, remote, virtual); `.nupkg`, `.snupkg`, tarballs, Maven files, OCI images |
| NuGet protocol | V3 feed URL (`.../nuget/v3/index.json`); legacy V2 (`.../nuget/v2`, PAT; Azure DevOps Services only, "only when you can't move to a newer client") | V3 service index, plus V2 OData feed `/api/v2` (on by default, `Feeds:EnableV2`) |
| Symbols | Symbol server, published by the `PublishSymbols@2` pipeline task; retention follows the producing build; `.snupkg` not supported | Symbol server in the same feed: push `.snupkg`, download by key; portable PDBs only |
| Upstream / proxy | Upstream sources (nuget.org, npmjs, ...); packages saved to the feed when a Collaborator+ installs them; custom upstreams only for npm; cannot publish a version that exists upstream | [Remote repositories](../remote-repositories.md) (NuGet, npm, Maven, Docker upstreams) and the optional `Mirror:Enabled` mirror; indexed copy kept; search does not query upstream |
| Multiple feeds / views | Many feeds (org or project scoped); views `@local`, `@prerelease`, `@release`, read-only; promotion only, no demotion | Many [repositories](../repositories.md); [virtual repositories](../virtual-repositories.md) merge several; no views |
| Private reads | Private feeds need Feed Reader or higher; public feeds only in public projects (public projects convert to private from 2027) | Yes: `AnonymousRead` per repository; a private one needs Basic auth or an API key |
| Permissions | Roles Reader, Collaborator, Contributor, Owner per feed; view visibility settable | [Access control](../access-control.md): private repositories, groups, permission targets, project roles; anonymous read per repository; Basic auth or API key for reads; accounts, organizations, per-package owners |
| Accounts / API keys | Azure DevOps identities, personal access tokens | Local accounts, OIDC, TOTP 2FA, scoped expiring API keys, global `ApiKey` |
| Retention | Policy: max versions per package and days since last download; deletions kept 30 days in Recycle Bin; versions immutable (number never reusable) | [Retention policies](../retention.md): keep newest N, age, unused, cache size, untagged Docker manifests, dry run, holds; delete = `Unlist` (default) or `HardDelete` |
| UI | Web UI in Azure DevOps | Web UI: search, details, upload, stats, manage |
| Hosting | Azure DevOps Services (SaaS) or Azure DevOps Server (on-prem; Universal Packages not in Server 2020/2022; Cargo not in Server 2020) | Self-hosted .NET 10 (Windows/Linux/macOS, Docker, IIS) |
| HA | Managed by Microsoft (SaaS); on-prem HA not verified | [High availability](../high-availability.md): several nodes on one database, object store and Redis (not on SQLite, the file system store or Azure Table) |
| Licence / cost | 2 GiB free per organization, then tiered from $2/GiB down to $0.25/GiB; publishing blocked above the limit until billing is set up | MIT, free; infrastructure cost only |
| Maintenance | Microsoft product; docs updated 2026 | Fork of loic-sharma/BaGet, community maintained |

## When to choose which

- Azure Artifacts: already on Azure DevOps, or you need Python/Cargo/Universal Packages, views or Microsoft-managed hosting.
- BaGet Extension: NuGet, npm, Maven and Docker, self-hosted, no per-GiB billing, V2 and V3 on one server, per-repository read permissions.

## Sources

Checked 2026-10.

- https://learn.microsoft.com/en-us/azure/devops/artifacts/start-using-azure-artifacts
- https://learn.microsoft.com/en-us/azure/devops/artifacts/concepts/feeds
- https://learn.microsoft.com/en-us/azure/devops/artifacts/concepts/views
- https://learn.microsoft.com/en-us/azure/devops/artifacts/concepts/upstream-sources
- https://learn.microsoft.com/en-us/azure/devops/artifacts/feeds/feed-permissions
- https://learn.microsoft.com/en-us/azure/devops/artifacts/how-to/delete-and-recover-packages
- https://learn.microsoft.com/en-us/azure/devops/pipelines/artifacts/symbols
- https://learn.microsoft.com/en-us/azure/devops/artifacts/nuget/nuget-exe
- https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg
- https://azure.microsoft.com/en-us/pricing/details/devops/azure-devops-services/
