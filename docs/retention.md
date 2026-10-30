# Retention and cleanup

Retention policies delete old versions on their own: keep the newest N, delete by age or by lack of downloads, trim a
remote cache, remove untagged Docker manifests and collect their blobs. Everything is in **Admin > Retention**
(`/admin/retention`); administrators only. Needs a relational database (not Azure Table).

## Policies

A policy applies to **one repository** or to **every repository of a project** (deleting the repository or the project deletes its policies, audited) (virtual repositories store nothing and
are skipped). A version is deleted when **any enabled rule selects it** and no legal hold covers it.

| Rule | Selects | Notes |
|---|---|---|
| Keep the newest N | versions of an artifact beyond its N newest (by publish time) | per artifact (package id, npm name, Maven `group:artifact`, Docker image) |
| Older than D days | published more than D days ago | |
| Unused for D days | last download older than D days | publish time (not before the rule started watching, see below) when no download was recorded (see below) |
| Prerelease only | restricts the three rules above to prerelease versions | NuGet pre-release, npm prerelease, Maven `-SNAPSHOT`; Docker tags are never prerelease |
| Cache limit (MiB) | remote repositories: least recently used versions until the cache fits | ignored (with a note) elsewhere |
| Untagged for D days | Docker manifests no tag, index or referrer relationship needs, pushed more than D days ago | counted from the push; children of an index and artifacts attached to a manifest (signatures) are kept |
| Collect garbage | Docker: run the [garbage collection](docker-registry.md) after the rules | frees the layers of deleted manifests |
| Only these artifacts | name globs (`*`, one per line) limiting the policy | empty = all |

What a "version" is per format:

| Format | Version | Deleted with |
|---|---|---|
| NuGet | package version | rows and `.nupkg`, as an administrator's hard delete (not the protocol's unlist) |
| npm | published version | row and tarball; dist-tags that pointed at it go, `latest` moves to the highest left; the last version removes the package |
| Maven | `group:artifact` version, with all files (snapshot builds included) | rows and files; the artifact goes with its last version |
| Docker | a **tag** of an image | the tag only; its manifest stays until the untagged rule takes it, its blobs until the garbage collection |

### Downloads and "unused"

NuGet counts downloads; the other formats do not, so the server records **when each version was last downloaded**
(NuGet package download, npm tarball, Maven file, Docker manifest pull by tag or digest). Writes are batched in memory and
saved about once a minute (and before a run plans), so a restart can lose the last minute. Downloads before this feature
existed are unknown: such a version counts from its publish time or from when the rule started watching, whichever is later,
so a policy never judges a period it did not watch (a new policy waits N days before it selects a version nobody is recorded
to have downloaded). The watching clock (`UnusedSince`) starts at creation and restarts when the *Unused* days, the repository,
the project or the name patterns are edited; an old policy that gains the rule, or is pointed at other repositories, waits N days too.

## Dry run, run, schedule

Open a policy (`/admin/retention/{id}`):

* **Dry run** lists every version the policy would delete (artifact, version, dates, size, the rule that selected it)
  and those kept by a legal hold. It deletes nothing and needs no password. **It is computed by the code a run uses**: the
  run deletes exactly that list (a version published, held or deleted in between aside). The Docker garbage collection in a
  dry run reports the state before this run's deletions.
* **Run now** (password) deletes the list. Both run in the background; the page shows the report when ready. One run per
  policy at a time. A version that cannot be deleted is reported `Failed` and tried again by the next run.
* **Schedule**: "run every N hours" (enabled policies only). The first run is N hours after the schedule was set or the policy
  enabled; then N hours after the last run started. Scheduled runs wait while the server is read-only (database copy) and
  run once it reopens.
* The result is kept on the policy (last run, one line). The detailed report of the latest run is kept in memory until restart.

A real run, saving, enabling and deleting a policy are refused with 503 while the server is read-only
([maintenance gate](configuration.md)); a dry run is a read and still works through the service.

## Legal holds

**Admin > Retention > Legal holds** (`/admin/retention/holds`). A hold names a repository, an artifact (in the form its
format lists it) and optionally one version (a Docker tag or digest). No version = every version, including ones published
later. Every rule skips a held version, and the report lists it as `Held`. A hold is only checked by retention: someone
who may delete a version still can. Placing a hold needs no password; lifting one does.

## Audit

Creating, changing and deleting a policy, placing and lifting a hold, and every run are written to the
[access audit](access-control.md#admin-pages) (`/admin/audit`, actions `retention.*`) and to the admin audit log
(`IAdminAudit`; runs are `retention.run` / `retention.dry-run`; a run the read-only gate interrupts is still audited as `retention.run` with "Interrupted" and what was deleted so far, the scheduler's actor is `scheduler`).

## Deleting a repository

Deleting a repository deletes its policies, legal holds and recorded download times.

## For developers

| Piece | Where |
|---|---|
| Tables `RetentionPolicies`, `RetentionHolds`, `ArtifactUsages` (migration `AddRetention` x4) | `BaGet.Core/Retention/RetentionEntities.cs` |
| Rules as a pure function | `RetentionEvaluator` |
| Run (plan + delete, per repository scope) | `RetentionEngine`; background runs `RetentionJobs`; schedule `RetentionWorker` |
| Per-format list/delete | `IFormatRetentionHooks`: `NuGetRetentionHooks`, `NpmRetentionHooks`, `MavenRetentionHooks`, `DockerRetentionHooks` |
| Policies and holds (audited) | `IRetentionAdministration` |
| Download times | `ArtifactUsageRecorder` (in memory, flushed in batches) |
