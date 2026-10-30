# TeamCity

[TeamCity](https://www.jetbrains.com/teamcity/) is the JetBrains CI/CD server. Its built-in NuGet feed serves packages produced by its own builds; it is not a general package registry.

| Feature | TeamCity built-in feed | BaGet Extension |
|---|---|---|
| Package formats | NuGet only | NuGet, npm, Maven, Docker |
| NuGet protocol | V1, V2, V3 (`.../app/nuget/feed/<project>/<feed>/v1`, `/v2`, `/v3/index.json`) | V3, plus V2 OData `/api/v2` |
| What it serves | `.nupkg` build artifacts, indexed per build configuration or project | Any pushed package |
| Push | Only from a build (feed URL + `%teamcity.nuget.feed.api.key%`); external pushes fail | Any client: `PUT /api/v2/package`, browser upload |
| Symbols | Not built in; separate TeamCity Symbol Server plugin (Symbol Files Indexer). TW-25512: symbol packages in the feed can break external source servers | Symbol server (portable PDBs) |
| Upstream / proxy | None found | [Remote repositories](../remote-repositories.md), optional mirror |
| Multiple feeds | Several feeds per project | Many [repositories](../repositories.md) per instance |
| Private reads | Basic auth needs "View project" permission; guest access optional | Yes: private repositories ([access control](../access-control.md)) |
| Permissions | TeamCity project permissions | Accounts, organizations, groups, permission targets, per-package owners |
| Accounts / API keys | TeamCity users, access tokens | Local accounts, OIDC, 2FA, scoped API keys |
| Retention | Tied to build artifacts: removed on artifact cleanup; pin the build to keep | [Retention policies](../retention.md), or manual `Unlist` / `HardDelete` |
| UI | TeamCity UI | Package search, details, stats, manage |
| Hosting | On-Premises, or TeamCity Cloud (JetBrains-hosted; details not verified) | Self-hosted .NET 10 |
| HA | On-Premises Enterprise: secondary HA nodes need no licence | [High availability](../high-availability.md): several nodes on one database, object store and Redis (not on SQLite, the file system store or Azure Table) |
| Licence / cost | Free Professional on-prem: 3 agents, 100 build configurations, 10 pipelines, community support; Enterprise paid | MIT, free |
| Maintenance | Active JetBrains product (2026.2 help) | Community fork |

## When to choose which

- TeamCity feed: packages are CI outputs consumed by other builds, with lifetime tied to the builds.
- BaGet Extension: a permanent feed that outlives builds, accepts pushes from any client, needs symbols or mirroring.
- Together: TeamCity builds and pushes to BaGet Extension with the NuGet Publish step.

## Sources

Checked 2026-10.

- https://www.jetbrains.com/help/teamcity/using-teamcity-as-nuget-feed.html
- https://www.jetbrains.com/help/teamcity/nuget.html
- https://www.jetbrains.com/help/teamcity/licensing-policy.html
- https://www.jetbrains.com/help/teamcity/teamcity-cloud.html
- https://blog.jetbrains.com/teamcity/2015/02/setting-up-teamcity-as-symbol-and-source-server/

Unconfirmed: TeamCity Cloud pricing and feature differences; the missing upstream support is inferred from the pages read.
