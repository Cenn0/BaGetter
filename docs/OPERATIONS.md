# Deployment and operations

This guide describes the **Cenn0/BaGetter fork**, not an independently released upstream binary.

## Deployment choices

For evaluation, run from source with the SDK specified in [global.json](../global.json), or build the repository [Dockerfile](../Dockerfile). The Docker image uses .NET 10 ASP.NET runtime and declares a persistent `/data` area. The Docker example in the [README](../readme.md) binds only to localhost; a production reverse proxy should terminate TLS and explicitly control authentication, publishing rights, allowed networks, body sizes, forwarding headers and request timeouts.

The application exposes a NuGet V3 service index at `/v3/index.json` and a configurable health-check path (default `/health`). Verify responses after deployment.

## Configuration

The committed defaults live in [src/BaGetter/appsettings.json](../src/BaGetter/appsettings.json). ASP.NET Core environment variables use double underscores for nesting. Important examples:

| Setting | Default in source | Operational consideration |
| --- | --- | --- |
| `Database__Type` | `Sqlite` | Choose and verify the intended persistence provider |
| `Database__ConnectionString` | `Data Source=bagetter.db` | Dockerfile overrides with a path under `/data/db` |
| `Storage__Type` | `FileSystem` | Storage backend must survive restarts |
| `Storage__Path` | empty | Dockerfile overrides to `/data` |
| `Mirror__Enabled` | `false` | Explicit opt-in to read-through caching |
| `Mirror__PackageSource` | `https://api.nuget.org/v3/index.json` | Upstream service index; requires HTTPS egress |
| `ApiKey` | empty | Do not treat an unset key as a validated publishing policy |
| `MaxPackageSizeGiB` | `8` | Coordinate reverse-proxy and storage limits |
| `HealthCheck__Path` | `/health` | Restrict/monitor appropriately |

Do not copy raw secrets into committed appsettings. Use protected runtime secrets, and validate how the configured authentication and package-publishing controls behave in your deployment. **No production-hardening guarantee is implied by defaults.**

## Persistence, backup and recovery

Back up the database and **all associated package/symbol blobs** as a consistent set. Backing up just SQLite or just files is insufficient. Schedule a maintenance window or take an application-consistent snapshot; ordinary directory copying while publishes run may create mismatched metadata and files. Validate restores into a separate instance before upgrading or retiring a backup.

For Docker, retain `bagetter-data` between containers; avoid anonymous volumes for durable hosting. Before any upgrade, snapshot storage/database and review changes in [ROADMAP.md](../ROADMAP.md) and the PR history. Avoid multi-replica writes to shared SQLite/filesystem without independently validated coordination.

## Atomically published package revisions

**Guarantee:** A new upload writes the `.nupkg`, `.nuspec`, and optional README/icon
to an immutable revision directory or object prefix first. The package row
stores `StorageRevision` and is published **only after** the content write
completes. Existing packages are updated inside one SQL transaction, or as a
single Azure Table entity operation. A reader resolves all content through
the committed revision pointer. Pre-upgrade package rows have a null revision
and keep working with their original storage layout.

This is **atomic visibility, not a distributed ACID/XA transaction**. The
database cannot enlist filesystem, S3, Azure Blob or other object stores in
one two-phase commit. A process crash may leave unreachable revision files,
but they are not advertised to NuGet clients. A database commit that survives
a storage-level durability failure can still produce a missing file. Operate
storage and database with appropriate durability settings and back them up
together. A missing mirrored file may be repaired from its authenticated
upstream; a manually published package must be restored from backup.

File storage organizes new artifacts as:

```text
packages/<lowercase-id>/<normalized-version>/
  revisions/<32-hex-revision>/
    <lowercase-id>.<normalized-version>.nupkg
    <lowercase-id>.nuspec
    readme            # optional
    icon              # optional
```

The optional filesystem collector checks for old unreferenced revisions every
six hours, with a two-day minimum age. It is disabled by default; enable it
only after verifying bounded upload durations and storage permissions:

```text
Storage__EnableRevisionCleanup=true
``` Cleanup reads the **current database row** before
deleting and fails closed if the DB is unavailable. A very long-running or
suspended upload (over two days) is not safe from a concurrent cleanup sweep;
do not use this unattended policy for environments permitting uploads blocked
for days. Cloud/object storage providers currently **do not have an automated
orphan scanner**; use provider lifecycle processes only with a verified
reference check or a separate reconciliation implementation. Never configure
a blind age-based lifecycle rule that could delete referenced content.

**Upgrade:** Apply database migrations and deploy one compatible version to
every serving replica before allowing writes. A node running the older build
cannot resolve revisioned package data and may write legacy paths. Do not mix
old and new binaries in a rolling deployment. Before an upgrade or rollback,
back up **both** the database and all object/file storage. A code-only
rollback without restoring the prior DB and blob snapshot is unsafe. Azure
Table Storage does not use the relational EF migrations, but its entities
need the new `StorageRevision` field to remain intact.

Hard delete removes the database row first and then its previously referenced
files. It is therefore **logically immediate but physically eventual** if
the process crashes between steps; filesystem cleanup can later reclaim
the old unreferenced revision. This implementation does not promise that
every deletion is durably or physically atomic across both resources.

## Operational validation

1. Verify `/health` and `/v3/index.json` using the public canonical HTTPS address.
2. From a separate NuGet client, restore a pinned test package through this feed.
3. If mirror mode is enabled, test a cold fetch and a warm repeat with **clean client-side NuGet caches**.
4. Test restart and package retrieval from persisted storage.
5. Check logs for authentication failures, package misses, upstream errors and storage/database exceptions.
6. Perform a periodic backup restore exercise.

See [MIRROR.md](MIRROR.md) for mirror-specific limitations and acceptance criteria.

## Public endpoint safeguards

Prefer a reverse proxy with valid TLS, explicit host/forwarded-header trust, access controls, request-size ceilings, rate limiting and network segmentation. Do not publish a write API just because read-only package consumption is required. Monitor disk utilization, HTTP errors and upstream latency. Verify permissions and provider behavior before claiming an installation is production-ready.

## Fork release destinations and validation

The tagged-release workflow belongs to **Cenn0/BaGetter**, not to upstream
`bagetter/BaGetter`. After successful source verification on a `vX.Y.Z` tag,
it creates a ZIP in this fork's GitHub Releases and builds a multi-architecture,
**version-tagged** container image at `ghcr.io/cenn0/bagetter:X.Y.Z`.
It does not automatically advance a `latest` tag. GHCR package visibility
and access must be configured separately if anonymous pulls are intended.

Publishing upstream NuGet package IDs, `bagetter/bagetter` Docker Hub tags or
the upstream Helm repository is deliberately **disabled**. Those actions require
fork-owned package identities, registry permissions and a separately reviewed
release plan; merely creating a tag here must not publish under an upstream name.

Before making a release, check the Linux/Windows Build matrix, the opt-in
[real nuget.org mirror smoke](../.github/workflows/mirror-smoke.yml), and the
[Docker startup/volume smoke](../.github/workflows/docker-smoke.yml). The
container check builds the actual Dockerfile, starts it, probes
`/v3/index.json` and `/health`, and restarts it with the same data volume.
This does **not** replace backup/restore or provider-migration qualification.

## PostgreSQL provider acceptance

The optional [PostgreSQL persistence smoke](../.github/workflows/postgresql-smoke.yml)
uses PostgreSQL 17 with the production application and applies the repository's
real EF Core migrations on startup. It publishes a pinned test package with
a temporary API key, confirms case-insensitive IDs, compares package bytes and
restarts the application using the same database and file store. This tests one
supported PostgreSQL configuration, not an exhaustive database-version matrix.
A PostgreSQL-only backup remains insufficient: package and symbol blobs must
also be backed up in the same consistent snapshot.

Until the MySQL/Pomelo provider has a verified EF Core 10 stable release
and a passing database migration/recovery matrix, keep the EF 9 provider
dependencies aligned and do not describe EF Core 10 provider support as
validated solely because the host itself uses .NET 10.
