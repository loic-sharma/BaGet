# High availability

Several BaGet nodes behind a load balancer, sharing one database, one object store and one Redis. **Off by default**: a
single node needs no Redis and behaves exactly as before.

## Requirements

| Need | Why | Refused at startup |
|---|---|---|
| A shared database: SQL Server, PostgreSQL or MySQL | every node reads and writes the same rows; the data protection keys live there | SQLite (a file on one machine), Azure Table |
| Shared storage: S3, Azure Blob, Google Cloud, Aliyun OSS | every node serves the same files | the file system (`Storage:Type` `Filesystem`) |
| Redis (any 6+), reachable from every node | leases, the read-only gate, rate limits | `Ha:ConnectionString` empty |
| A balancer that probes `/health/ready` | a node that cannot reach its database or storage leaves rotation (Redis is reported only) | - |

A node that breaks a rule logs it at critical level and does not start (`HaStartupGuard`, and `ValidateStartupOptions` before the host starts).

## Settings (`Ha:*`, all need a restart)

| Key | Default | Meaning |
|---|---|---|
| `Ha:Enabled` | `false` | Cluster mode |
| `Ha:Backend` | `Redis` | The only backend |
| `Ha:ConnectionString` | empty | Redis connection string (`host:6379,password=...,ssl=true`); sensitive, never shown or logged |
| `Ha:NodeName` | machine name + random suffix | Name on the Cluster page and in lease holders |
| `Ha:KeyPrefix` | `baget` | Prefix of every Redis key, so clusters can share a Redis |
| `Ha:LeaseDuration` | `00:00:30` | A lease or gate of a node that stopped answering frees after this; holders renew every third of it (min 3 s) |
| `Ha:SyncInterval` | `00:00:02` | Heartbeat and pick-up of what other nodes changed (min 100 ms) |
| `Ha:KeyCertificatePath` | empty | PFX (with private key, the same on every node) that encrypts the data protection keys stored in the database. **Set it**: without it the keys sit as plain XML next to the secrets they protect (remote passwords, replication peer keys) and the node logs a warning at start |
| `Ha:KeyCertificatePassword` | empty | Password of that PFX; sensitive |

They are on **Server settings > Ha** (connection string masked). Give every node the same database, storage, `Ha:KeyPrefix`
and Redis. `Ha:NodeName` may differ or be left empty.

## What is shared

Every singleton or static holding state (B4), by where it lives in a cluster:

| State | Class | In a cluster |
|---|---|---|
| Read-only gate | `MaintenanceGate` | **Shared.** `Close` is published to Redis (key `gate`, renewed by the closing node, freed if it dies); every node refuses writes. Only the closing node reopens it. `CloseUntilRestart` stays on its node |
| Database copy progress | `DatabaseCopyService` | **A copy is refused while `Ha:Enabled`** (the Settings page shows why): the other nodes cannot be confirmed read-only before it reads, and after it succeeds they would reopen and write to the old database. To move a cluster: stop the other nodes, turn `Ha:Enabled` off, copy, restart, re-enable. Progress is still shared so a node shows a copy it did not start |
| Contact-owners limit, password-reset limits | `ContactRateLimiter`, `PasswordResetRateLimiter` | **Shared** (Redis keys with expiry; the client cap is a fixed window) |
| Data protection keys (cookies, antiforgery, protected remote and replication credentials) | key ring | **Shared** in table `DataProtectionKeys`, one application name. Credentials protected before HA was enabled used the old key ring: enter them again. Encrypt the ring with `Ha:KeyCertificatePath`. A key is written through a context the read-only gate does not block, so sign-in keeps working while the cluster is read-only. A key created while a (non-cluster) database copy runs may be missing from the copy: sessions and protected credentials made with it fail after the restart, enter them again |
| Access model | `AccessModelCache`, `AccessModelVersion` | **Per-node cache, shared invalidation.** A committed change bumps a Redis counter; the others reload within `Ha:SyncInterval` (30 s at the latest) |
| Job leases | `IJobLease` | **Shared** (`ClusterJobLease`); see below |
| Scans in progress | `ScanInFlight` | **Shared marker** per image (so the scanner's pull is recognised on whichever node receives it) |
| Settings overrides file | `SettingsOverridesStore` | **Per node** (`appsettings.Overrides.json`). Keep one file for all nodes (a shared volume, `SettingsOverridesPath`) or configure by environment; all settings that matter here need a restart anyway |
| Remembered upstream answers, counters, pooled clients | `RemoteRepositoryState` | **Per node** (TTL based). A purge on one node clears that node; others expire theirs by TTL |
| Remote downloads in progress | `KeyedLocks`, `RemoteOciRegistry.Caching` | **Per node.** Two nodes may fetch the same file; the second store is a no-op |
| Job status and last reports | `AdminJobService`, `RetentionJobs`, `ReplicationJobs` | **Per node** (memory); the run itself is leased. The page shows the status of the node you reached |
| Scan queue, policy and advisory caches | `ScanQueue`, `ScanPolicyCache`, `OsvState` | **Per node** |
| Download times | `ArtifactUsageRecorder` | **Per node**, flushed to the database in batches |
| Validation queue | `InMemoryValidationQueue` | **Per node**; one validator per submission (lease); every minute a cluster node offers all pending submissions again, so a node that died mid-validation is not waited for |
| Docker blob commit fence | `OciBlobCommits` | **Per node**: the garbage collector's "files without a row" step is **skipped in a cluster** (its report says so) because it cannot see another node's commit |
| E-mail queue | `BackgroundEmailQueue` | **Per node** (mail queued on a node that stops is lost, as on a single node) |
| Telemetry status and exporters | `TelemetryStatus` | **Per node**: each node exports its own signals; set `Telemetry:ServiceName` per node if you want them apart |
| Format registry, framework tables, factories | stateless | identical on every node |

## Jobs run once

The lease is a Redis key only one node can create, holding `node|token|since`; it expires after `Ha:LeaseDuration` unless the
holder renews it (every third). A node that cannot reach Redis **skips** the job instead of running it twice.

| Job | Lease |
|---|---|
| Retention schedule pass (all due policies) | `retention:schedule` |
| A retention run, scheduled or from the page | `retention:policy:{key}` (a dry run takes none) |
| Replication run, resume, orphan sweep | `replication:{config key}` |
| Admin operation (catalog rebuild, import downloads, Docker GC, vulnerability scan) | `operation:{name}:{repository}` |
| Scheduled vulnerability rescan | `scan:sweep` |
| Abandoned-upload sweep | `staging:sweep` |
| Validation of one submission | `validation:{submission id}` |
| A catalog commit (a commit waits up to 30 s for it, so timestamps stay in order across nodes; with the store unreachable it commits without it, warning) | `catalog:{repository key}` |
| Docker garbage collection schedule | runs inside a retention policy (`CollectGarbage`), so under its lease |

## Health

| Endpoint | Answer |
|---|---|
| `GET /health/live` | `200 Healthy` while the process answers |
| `GET /health/ready` | `200` or `503` with `{"status", "checks": {"database", "storage", "cluster"}}`; `503` only when `database` or `storage` fails. `cluster` (the Redis ping) appears only with HA and is **reported, never a cause of `503`** (a node keeps serving without Redis); checked at most every 5 s |

The storage check writes and deletes a one-byte object under `health/`. The body never carries a message or a connection
string. Readiness ignores the read-only gate: a node refusing writes still serves reads.

## Cluster page

**Admin > Cluster** (`/admin/cluster`): the nodes with a heartbeat in the last seconds (version, start, readiness, read-only), the leases held
and by whom, whether the cluster is read-only and why, and this node's health checks (`Check again` re-runs them).

## Redis down

Nodes keep serving. Leases are refused (jobs wait), the gate and access model stop propagating (the access model still reloads
every 30 s) and the rate limiters of a node fail their page with an error until Redis returns. `/health/ready` stays `200` (a Redis
outage would otherwise take every node out of rotation at once); `cluster` shows `Unhealthy` in its body and on the Cluster page.

## Tests

`ClusterTests` (`BaGet.Tests`) run two real nodes in one process on one SQLite file, one package folder and an in-memory store
(`ClusterHarness`; `HaBackendRules.AllowLocalBackends` lifts the startup refusal for the test only). `BaGet.Ha.Tests` runs the
store contract and the same two nodes against a real Redis in Testcontainers (skips without Docker).
