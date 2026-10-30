# MyGet

[MyGet](https://www.myget.org) is a hosted package management service for NuGet and other formats.

| Feature | MyGet | BaGet Extension |
|---|---|---|
| Package formats | NuGet, symbols, npm, Bower, Maven, PHP Composer, VSIX, Python, RubyGems | NuGet, npm, Maven, Docker |
| NuGet protocol | V3 (`/F/<feed>/api/v3/index.json`), V2 (`/api/v2`), V1 | V3, plus V2 OData `/api/v2` (on by default) |
| Symbols | Symbol server per feed (`/F/<feed>/symbols`) | Symbol server in the feed; portable PDBs only |
| Upstream / proxy | Proxy (metadata only) or mirror (copy on first request); mirrored packages count toward quota; update-check interval configurable (the Professional plan's 30-min interval was documented, but that plan is no longer sold) | [Remote repositories](../remote-repositories.md) (copy kept) and an optional `Mirror:Enabled` mirror; search does not query upstream |
| Multiple feeds | Private feeds per plan (1 / 5 / unlimited); public feeds on all plans | Many [repositories](../repositories.md) per instance, no plan limit |
| Private reads | Private feeds require basic auth or an API-key URL | Yes: private repositories, Basic auth or API key ([access control](../access-control.md)) |
| Permissions | Contributors per plan (1 / 5 / 10+); identity providers (Azure AD, ADFS, Okta) supported | Accounts, organizations, groups, permission targets, project roles, per-package owners |
| Accounts / API keys | MyGet accounts, access tokens, 2FA | Local accounts, OIDC, TOTP 2FA, scoped expiring API keys |
| Retention | Package retention feature exists (details not verified); custom retention available | [Retention policies](../retention.md); `Unlist` or `HardDelete` |
| UI | Web UI | Web UI |
| Hosting | SaaS; Enterprise on dedicated Azure infrastructure | Self-hosted .NET 10 |
| HA | Provider-managed | [High availability](../high-availability.md): several nodes on one database, object store and Redis (not on SQLite, the file system store or Azure Table) |
| Licence / cost | Developer $15/mo (500 MB), Team $40/mo (2 GB), Enterprise from $1,395/yr (4 GB); max package size 500 MB / 1 GB | MIT, free |
| Maintenance | Owned by Idera (via Assembla, 2018); operating (2026-09); docs' last release note 2020.1 | Community fork |

## When to choose which

- MyGet: hosted feed with no server to run, several formats, build services, license and vulnerability governance (Team and up).
- BaGet Extension: NuGet, npm, Maven and Docker, self-hosted, no plan limits on feeds, storage or contributors.
- Private reads: both have them.

## Sources

Checked 2026-10.

- https://www.myget.org/pricing
- https://www.myget.org
- https://docs.myget.org/docs/reference/feed-endpoints
- https://docs.myget.org/docs/reference/upstream-sources
- https://www.ideracorp.com/pressreleases/acquires-assembla

Unconfirmed: long-term roadmap; retention details; the docs still mention a "Professional" plan that the pricing page no longer lists.
