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
