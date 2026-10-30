# BaGet Extension

BaGet Extension is A NuGet, Maven, NVM, Docker and symbol server. It is [open source](https://github.com/pieroviano/BaGet) (a fork of [loic-sharma/BaGet](https://github.com/loic-sharma/BaGet)), cross-platform, and cloud ready!

<p align="center">
  <img width="100%" src="BaGetSite.png">
</p>

New here? Read the [installation manual](installation-manual.md) and the [user manual](user-manual.md); operators: the [DevOps manual](devops-manual.md); common tasks: [how-to](howto.md).

## Run BaGet Extension

You can run BaGet Extension on your preferred platform:

* [On your computer](installation/local.md)
* [Docker](installation/docker.md)
* [Azure](installation/azure.md)
* [AWS](installation/aws.md)
* [Google Cloud](installation/gcp.md)
* [Alibaba Cloud (Aliyun)](installation/aliyun.md)
* [Windows, behind an IIS proxy](installation/iis-proxy.md)

A fresh install has one account, the [default administrator](configuration.md#default-administrator) `Admin` / `Admin1234!`: sign in and change its password at once.

## Repositories

One server can hold several [repositories](repositories.md), each with its own feed at `/r/{name}/v3/index.json`; `default` stays at `/`. Repositories have a [format](formats.md): NuGet, and [npm registries](npm-registry.md) (`npm publish` / `install`, dist-tags, deprecate, unpublish, `npm login`), [Maven repositories](maven-repositories.md) (`mvn deploy`, snapshots, checksums, release/snapshot policy) and a [Docker registry](docker-registry.md) (`docker login` / `push` / `pull`, multi-platform images, referrers). A [remote repository](remote-repositories.md) caches another NuGet feed, npm registry, Maven repository or Docker registry such as registry.npmjs.org or Docker Hub (credentials, cache periods, offline mode, purge); a [virtual repository](virtual-repositories.md) puts several of them behind one URL (first match wins, merged search and packuments, uploads to a chosen member). [Retention policies](retention.md) delete old versions on a schedule (keep the newest N, by age, unused, remote cache size, untagged Docker manifests) with a dry run and legal holds. [Replication](replication.md) copies a local repository to or from a repository of another BaGet server (push or pull, on deploy and on a schedule, resumable, any format). [High availability](high-availability.md) runs several nodes behind a load balancer on one database, object store and Redis (jobs run once, the read-only gate and rate limits are shared, `/health/live` and `/health/ready`). The [Settings page](configuration.md) edits configuration live, and OpenTelemetry export (traces, metrics, logs) needs an OTLP endpoint.

## Access control

Who may read and write what is set by [groups, permission targets and project roles](access-control.md): private feeds,
Basic-auth client setup, upgrade defaults and the admin pages.

## Vulnerability scanning

Packages, npm and Maven artifacts are checked against [OSV.dev](https://osv.dev) (online or an offline copy) and Docker images by
Trivy; findings appear on every package page and in an administrator dashboard, feed NuGet's vulnerability resource, and a
per-repository or per-project policy can warn or refuse downloads and deploys with `403` and the reason:
[vulnerability scanning](vulnerability-scanning.md).

## Administration REST API

`/api/v1` does everything the administrator pages do (repositories, access model, users, operations, retention, scanning, replication) for
scripts, with an administrator's API key and OpenAPI 3: [administration REST API](admin-api.md); try every endpoint in the browser: [testing the APIs with Scalar](scalar.md). Every web screen also has a JSON twin for custom frontends: [UI API](ui-api.md); build your own in React or Angular: [custom frontend](custom-frontend.md).

## What BaGet Extension serves

BaGet Extension speaks the NuGet V3 protocol, with a V2 feed for older clients (on by default), and serves
symbols; npm, Maven and Docker are served per repository (above). Accounts with package owners and per-user API keys are on by default; a validation
pipeline with repository signing is opt-in. Both are set in its [configuration](configuration.md).
By default it listens on `http://localhost:6060` (`Kestrel:Endpoints`; `--urls` has no effect). The [server API](advanced/api.md) lists
every resource and page, [UI coverage](ui-coverage.md) maps every capability to its page, and [BaGet Extension vs nuget.org](vs/nugetorg.md) compares the two.

## Import packages

* [From nuget.org](import/nugetorg.md)
* [From NuGet.Server](import/nugetserver.md)
* [From a local feed](import/local-feeds.md)

## Compare BaGet Extension

* [nuget.org](vs/nugetorg.md)
* [Artifactory](vs/artifactory.md)
* [Azure Artifacts](vs/azure-artifacts.md)
* [LiGet](vs/liget.md)
* [Local feeds](vs/local-feeds.md)
* [MyGet](vs/myget.md)
* [Nexus](vs/nexus.md)
* [NuGet.Server](vs/nugetserver.md)
* [TeamCity](vs/teamcity.md)

## Advanced

* [Architecture](advanced/architecture.md)
* [Server API](advanced/api.md)
* [Debugging BaGet Extension](advanced/debugging.md)

## BaGet Extension SDK

You can also use the [`BaGet.Protocol`](https://www.nuget.org/packages/BaGet.Protocol) package to interact with a NuGet server. For more information, please refer to the [BaGet Extension SDK](advanced/sdk.md) guide.

To browse these pages without a site, see [reading this folder](https://github.com/pieroviano/BaGet/tree/main/docs).

## Credits

BaGet Extension is based on [BaGet](https://github.com/loic-sharma/BaGet) (pronounced "baguette") by Loic Sharma,
released under the MIT licence. See [LICENSE](https://github.com/pieroviano/BaGet/blob/main/LICENSE) and
[Acknowledgments](https://github.com/pieroviano/BaGet/blob/main/Acknowledgments.md).
