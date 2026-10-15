# nuget.org read-through mirror

## Scope and status

The fork includes **optional package read-through caching**; it is not yet a generally certified, transparent nuget.org replacement. Full upstream search/autocomplete proxying and end-to-end offline resolution of arbitrary packages are not established features. [ROADMAP.md](../ROADMAP.md) defines the release acceptance matrix. Open PRs are not merged functionality: consult [pull requests](https://github.com/Cenn0/BaGetter/pulls) and [CI](https://github.com/Cenn0/BaGetter/actions) before assuming a proposed fix is available on `main`.

## Enabling a cache

In your local runtime configuration, set:

```text
Mirror__Enabled=true
Mirror__PackageSource=https://api.nuget.org/v3/index.json
```

For Docker, pass these as environment variables to **the locally built fork image** and persist `/data`. Restrict upstream egress to the intended endpoints where feasible, while allowing the endpoint discovery required by the NuGet V3 protocol. Use the self-hosted service index at `https://your-feed.example/v3/index.json` as your NuGet package source (replace the example hostname with your real TLS endpoint).

Do not publish the example hostname as a working URL; it is illustrative only.

## Expected behavior versus guarantees

| Scenario | Intended behavior | Qualification needed |
| --- | --- | --- |
| Package exists in origin, absent locally | Fetch, validate, index and cache | Real nuget.org restore and byte comparison |
| Cached package, origin unavailable | Serve available cached package files | Restart and blocked-egress test |
| Uncached package, origin unavailable | Return an explicit failure, not a fabricated package absence | Outage and HTTP mapping tests |
| 32 concurrent cold requests | Single-process coordinated fetch without corrupt results | Real HTTP concurrency and upstream count |
| Cache metadata exists, file missing | Controlled origin-based recovery, never overwrite local publish | Fault injection and PR qualification |
| NuGet package search / autocomplete | No unconditional full-proxy guarantee | Separate protocol coverage |

A cached **package file** alone does not guarantee that all version, registration or search metadata can be resolved offline. NuGet client caches must be cleared in acceptance tests to ensure the server cache is actually exercised.

## Minimal manual verification

1. Enable mirror mode against the real `https://api.nuget.org/v3/index.json` service index in an isolated test deployment.
2. Create a fresh test .NET project and a dedicated `NuGet.Config` containing **only the BaGetter source**; do not allow silent fallback to nuget.org.
3. Restore a pinned dependency and its transitives. Verify package download succeeds and note the version/identity.
4. Clear the client global-packages and HTTP caches; restore again to exercise a warm server cache.
5. Restart the server with persistent database and blob storage. Repeat. For offline qualification, block upstream egress and repeat with cleared client caches.
6. Compare the resulting `.nupkg` bytes to the origin; check prereleases, normalized IDs, index and manifest routes.
7. Check HTTP logs for genuine 404 (confirmed absence) versus 503/other infrastructure failures.

Run the repository's dedicated [mirror smoke workflow](https://github.com/Cenn0/BaGetter/actions/workflows/mirror-smoke.yml) when available. Check actual workflow results; the presence of a workflow file is not evidence of a passing run.

## Operational risks

The cache/database publication path does not provide a distributed atomic transaction. Per-package coordination in a single process cannot prove multi-replica safety. Do not assume consistency under shared mutable storage or network outages. Cached data should be treated as recoverable only when backed up or reproducibly fetched from the original trusted source. Locally published packages must never be replaced by upstream repair.

For priority, exit criteria and upstream PR references see [ROADMAP.md](../ROADMAP.md).
