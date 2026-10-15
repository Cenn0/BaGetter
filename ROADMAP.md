# BaGetter Fork Roadmap

> **Scope:** Cenn0/BaGetter (independent fork; not an upstream BaGetter commitment)  
> **Reviewed:** 2026-10-15  
> **Baseline:** main at 1266b053 (after fork PRs #1–#5); .NET 9; 385 tests passing on each of Ubuntu and Windows in [Build #38713873519](https://github.com/Cenn0/BaGetter/actions/runs/38713873519).  
> **Inputs:** [upstream open PRs](https://github.com/bagetter/BaGetter/pulls), [upstream issues](https://github.com/bagetter/BaGetter/issues), current fork source and [Microsoft's NuGet V3 server implementation guide](https://learn.microsoft.com/en-us/nuget/api/implementation-guide).

This is a **priority- and release-gate-based roadmap**, not a set of promised delivery dates. All upstream PRs are **candidate implementations**, not code already present in this fork or an assurance of compatibility.

## Product direction

Make BaGetter a **reliable, secure, self-hosted NuGet V3 feed**, with a primary emphasis on a dependable nuget.org upstream.

Two operational modes must remain distinct:

1. **Read-through mirror (required first):** on first request, download a particular package from nuget.org, validate it, cache it, and serve repeat requests locally. The existing fork supports this in principle, but it has not yet passed a real nuget.org end-to-end qualification.
2. **Full proxy (optional, later):** also proxy upstream search/autocomplete, relevant metadata, and permitted assets. In strict-proxy mode clients should only need BaGetter for core NuGet protocol requests. No upstream URI may leak into BaGetter's service-index resource URLs in that mode; unrelated third-party links contained in package metadata are not automatically safe or supported for proxying.

The immediate release blocker is **mirror correctness**, **not a new user interface**.

**Implementation log (2026-10-15):** Initial G1 work adds HTTP upstream-error classification and an explicit 503 response path instead of silently converting all failed upstream package downloads into HTTP 404. This change requires dedicated CI qualification and does not complete G1. Prefer incremental, independently testable changes over cherry-picking very large, mutually overlapping PRs.

### Existing fork baseline — do not reimplement blindly

- [Fork PR #1](https://github.com/Cenn0/BaGetter/pull/1): fail-closed write authorization, private-feed UI policy, HTML sanitization, CORS and forwarded-header hardening.
- [Fork PR #2](https://github.com/Cenn0/BaGetter/pull/2): single-process coordination for concurrent cache misses and retention fixes; conceptually overlaps upstream [#290](https://github.com/bagetter/BaGetter/pull/290), [#288](https://github.com/bagetter/BaGetter/pull/288), and part of [#294](https://github.com/bagetter/BaGetter/pull/294).
- [Fork PR #3](https://github.com/Cenn0/BaGetter/pull/3): atomic final-file publication on the filesystem. **This is not an atomic database-plus-storage transaction.**
- [Fork PR #4](https://github.com/Cenn0/BaGetter/pull/4): finite request-body limits.
- [Fork PR #5](https://github.com/Cenn0/BaGetter/pull/5): regression fixes; successful cross-platform CI.

The 385 existing tests per operating system do **not** establish offline-cache correctness, actual nuget.org connectivity, multi-replica safety, or full-proxy interoperability.

## Priority and release gates

| Gate | Priority | Goal | Primary upstream references | Exit criteria |
| --- | --- | --- | --- | --- |
| **G0 — Engineering baseline** | P0 | Reliable CI and .NET 10 LTS migration | [#269](https://github.com/bagetter/BaGetter/pull/269), [#292](https://github.com/bagetter/BaGetter/pull/292), [#278](https://github.com/bagetter/BaGetter/pull/278) | CI green on Linux/Windows, supported .NET baseline, migration/backup checks |
| **G1 — nuget.org mirror correctness** | **P0 / release blocker** | Real restores, integrity, race safety, offline cache hits | [#290](https://github.com/bagetter/BaGetter/pull/290), [#294](https://github.com/bagetter/BaGetter/pull/294), [#288](https://github.com/bagetter/BaGetter/pull/288) | The mirror acceptance matrix below passes |
| **G2 — Efficient mirror operation** | P1 | Bounded upstream traffic, cache lifecycle and observability | [#269](https://github.com/bagetter/BaGetter/pull/269), [#283](https://github.com/bagetter/BaGetter/pull/283), [issue #267](https://github.com/bagetter/BaGetter/issues/267) | Warm-cache requests behave as documented; timeouts/failures measurable |
| **G3 — Discoverable full proxy** | P1/P2 | Upstream search/autocomplete and secure asset routing | [#285](https://github.com/bagetter/BaGetter/pull/285), [#284](https://github.com/bagetter/BaGetter/pull/284), [#265](https://github.com/bagetter/BaGetter/pull/265) | NuGet/Visual Studio search, metadata and strict-proxy checks pass |
| **G4 — Security and production operations** | P1/P2 | Package policies, safe storage, backup/restore and release pipeline | [#283](https://github.com/bagetter/BaGetter/pull/283), [#249](https://github.com/bagetter/BaGetter/pull/249), [#176](https://github.com/bagetter/BaGetter/pull/176) | Recovery exercises, hardened deploys and monitored service |
| **G5 — Advanced hosting** | P3 | Multi-feed, SSO/RBAC, provider and UI enhancements | [#278](https://github.com/bagetter/BaGetter/pull/278), [#272](https://github.com/bagetter/BaGetter/pull/272), [#280](https://github.com/bagetter/BaGetter/pull/280) | Explicit product requirements, upgrade tests and isolated permissions |

G0 and G1 should be pursued in parallel, but neither should be declared complete merely because an upstream PR claims successful tests. Microsoft's [.NET support policy](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support) lists .NET 9 support ending in November 2026; this makes G0 time-sensitive.

## G1: nuget.org mirror — detailed work plan and mandatory acceptance

### M1.1 — Reproducible end-to-end verification (first task)

- Exercise the actual [nuget.org V3 service index](https://api.nuget.org/v3/index.json) over HTTPS; do not hard-code resource endpoints discovered through that index.
- Test both a deterministic in-process fake upstream (required on every PR) and a separate **opt-in/nightly real nuget.org smoke suite**. Upstream availability must never make ordinary PR builds flaky.
- Stand up BaGetter with mirror enabled, empty database, empty file storage and an isolated NuGet global-packages directory. Restore a small pinned sample project through **only the BaGetter feed**; verify direct and transitive dependencies.
- Test a cold restore, repeat restore with a clean client package cache (server cache warm), restart the server, then block upstream egress and restore only previously cached package versions again. Explicitly report which metadata requests still attempt upstream access.
- Compare downloaded .nupkg bytes/checksums to the upstream source, never repack or alter signed packages. Verify manifest, version index, registration index/leaf, normalized/case-insensitive package IDs, SemVer2 and prerelease versions.
- Probe standard NuGet CLI and Visual Studio package consumption, plus the GET/HEAD behavior declared by [NuGet's PackageBaseAddress specification](https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource).

**Exit:** No unexplained failed restore or divergent package bytes in the test matrix; a cached package can be restored with upstream unavailable, with the test project's dependencies also cached. Cache misses and failures are logged distinctly.

### M1.2 — Correct cache publication and recovery

- Review the current flow in PackageService, PackageIndexingService, PackageDatabase and PackageStorageService as one state machine. A metadata row is not proof that the package and its supporting files exist.
- Verify that every advertised version is actually downloadable, as required by the [PackageBaseAddress specification](https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource).
- Add consistency checks and a reconciliation/repair path for: interrupted downloads, incomplete files, database commit failure, storage failure, interrupted startup, stale metadata, and deliberately deleted cached files.
- For **mirrored** content that is corrupt or missing, permit controlled re-fetch with integrity checks. Never silently replace a locally published immutable version.
- Introduce a validated staging/published state or an equivalent recoverable write protocol; never expose a partially written package, and record/clean abandoned staged content.
- Return actual **404 only for a confirmed missing package**; distinguish upstream timeouts, HTTP 429, 5xx, DNS/TLS failures and local storage/database failures rather than translating all of them into "package not found". Define and test downstream error mapping.
- Preserve cancellation, bounded retry/backoff with jitter, upstream Retry-After where applicable, and configurable timeouts. Avoid uncontrolled retry storms.

**Exit:** Fault-injection tests prove that partial state is neither presented as valid nor permanently poisons future requests; reconciliation restores known-good cached packages without modifying locally pushed packages.

### M1.3 — Concurrency, retention and multi-instance behavior

- Keep the existing process-wide per-package coordination from fork PR #2, but test it using a **real HTTP mirror and a local fake nuget.org**, not only mocked service methods.
- Acceptance scenario: **32 concurrent cold requests for the same ID/version** produce no spurious 404/500; one upstream download/index in the supported single-process configuration. Track and assert actual upstream request count.
- Run concurrent requests for different packages to detect semaphore contention. Check cancellation while waiting and while downloading.
- Test same-ID/version clashes across two application replicas sharing supported storage/database. A local semaphore is **not** a distributed lock; either implement distributed coordination or document and enforce the supported topology.
- Ensure retention never deletes a package before the triggering download is served; add policy for actively served/cached packages and separate **cache eviction** from retention of locally published releases.
- Recheck [upstream #290](https://github.com/bagetter/BaGetter/pull/290), [#294](https://github.com/bagetter/BaGetter/pull/294), [#288](https://github.com/bagetter/BaGetter/pull/288) for missing edge cases, without overwriting the existing fork fixes.

**Exit:** The above concurrency tests pass, including retention-enabled restores. The support statement clearly distinguishes single-instance from multi-replica guarantees.

### M1.4 — Correct metadata and client compatibility

- Ensure flat-container version listings, registration responses and available package files remain consistent on cold, warm and unavailable-upstream paths.
- Preserve package ID and version normalization; test legacy/modern NuGet clients and supported version semantics.
- Test mixed public mirrored and private pushed packages for identical IDs, version conflicts, provenance and explicit precedence; avoid dependency-confusion surprises.
- Validate NuGet service-index URLs behind HTTPS reverse proxies and path bases; no accidental links to localhost or upstream in a strict-proxy configuration.
- Include non-ASCII package author/title metadata and database provider checks (notably MySQL), as documented in [upstream issue #214](https://github.com/bagetter/BaGetter/issues/214).

**Exit:** A pinned sample solution restores with BaGetter as its only source on supported platforms, including cold/warm repeat runs and a controlled upstream outage.

## G2: efficient, observable mirroring

1. **Local-first reads and metadata cache:** PackageService currently consults upstream in FindPackageVersionsAsync and FindPackagesAsync even where local versions are known. Define a TTL/stale-on-error model; use conditional requests where supported, and prevent repeat metadata lookups on every warm request. Address [issue #267](https://github.com/bagetter/BaGetter/issues/267). Do not assume every upstream supports ETag/Last-Modified.
2. **Error-aware negative caching:** Cache genuine misses only briefly; never cache transient 429, 5xx, timeouts or DNS errors as permanent 404s. Make durations configurable.
3. **Cache provenance and limits:** Build on existing CachedFrom metadata; distinguish upstream-cached packages from manually published ones. Add per-origin cache metrics, cache size budget, eviction policy, pinning, dry-run garbage collection and safe pruning. See [issues #187](https://github.com/bagetter/BaGetter/issues/187), [#118](https://github.com/bagetter/BaGetter/issues/118), [#277](https://github.com/bagetter/BaGetter/issues/277).
4. **Observability:** Count upstream requests by reason/outcome, downloads, bytes, cache hit/miss, cache warm latency, stale metadata served, 429, 5xx, validation errors, origin timeouts and coalesced concurrent requests. Expose actionable logs, metrics and health/readiness without leaking credentials.
5. **Load and database evaluation:** Benchmark simultaneous NuGet restores against SQLite and PostgreSQL; investigate [issue #229](https://github.com/bagetter/BaGetter/issues/229). Avoid a write transaction per downloaded package if it becomes a bottleneck.

**Exit:** Tests demonstrate no redundant .nupkg download on a warm hit, controlled metadata freshness, no false 404 caused by upstream outages, and measured upstream request reduction. Publish the actual measurements rather than making assumed performance claims.

## G3: searchable and secure full proxy

Only start this gate after the core mirror is qualified.

- **Upstream discovery:** Adapt [PR #285](https://github.com/bagetter/BaGetter/pull/285) in separate PRs for upstream search, autocomplete, registration/metadata integration, and optional asset handling. Include real NuGet/Visual Studio UX tests, query pagination, prerelease, SemVer2 and locally published package precedence. Today's local database search intentionally omits uncached upstream packages.
- **Proxy-mode contract:** Default behavior stays read-through. An optional strict full-proxy mode must advertise only authorized BaGetter-hosted NuGet resource URLs and must not depend on direct nuget.org client connectivity for core restore/search metadata.
- **Asset security:** Review #285's host discovery and SSRF mitigations: HTTPS-only upstream origin policy, DNS resolution/rebinding, loopback/private networks, redirects, response content types, maximum asset sizes, response limits and upstream credential isolation. Author-supplied third-party project/license URLs are not automatically trusted for server-side fetching.
- **Metadata fidelity:** Embedded icons, license, README and deprecation metadata must remain accessible via correctly advertised URLs; evaluate [PR #265](https://github.com/bagetter/BaGetter/pull/265) and [issue #289](https://github.com/bagetter/BaGetter/issues/289). Do not infer features from an endpoint's existence alone.
- **Vulnerability data:** Follow the [NuGet server implementation guide](https://learn.microsoft.com/en-us/nuget/api/implementation-guide) for V3 registration deprecations/vulnerabilities and the optional VulnerabilityInfo resource. Use the nuget.org Catalog where appropriate to refresh metadata, with a cursor, and explicitly define what offline clients receive.
- **Client policy:** Document [NuGet Package Source Mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping) when a project uses more than one source. With strict proxy, an example NuGet.Config should contain only the BaGetter package source for package resolution.

**Exit:** Upstream packages are discoverable in Visual Studio and CLI; the pinned sample solution restores through BaGetter alone; normal protocol requests do not require client egress to nuget.org; security tests reject SSRF and unauthorized proxy requests.

## G0/G4: runtime, CI, security and operations

- **.NET 10 LTS:** Isolate the runtime/SDK/EF-provider migration from the sweeping UI/identity changes proposed in [#269](https://github.com/bagetter/BaGetter/pull/269), [#278](https://github.com/bagetter/BaGetter/pull/278) and [#292](https://github.com/bagetter/BaGetter/pull/292). Update base images and all supported database providers; run migrations and restore tests on existing datasets. Validate packages and transitive NuGet dependencies, not just a green compilation.
- **CI quality gates:** Unit/integration matrix on Windows/Linux; PostgreSQL+SQLite persistence; deterministic mock-upstream fault injection; real nuget.org nightly/manual smoke test; Docker image startup smoke test. Block merge on regressions. Existing unrelated Helm workflow failures should be isolated and fixed rather than ignored.
- **Security policies:** Consider [#283](https://github.com/bagetter/BaGetter/pull/283) for denylisting upstream IDs/version ranges. Then assess upstream package age and advisory policies from [issue #257](https://github.com/bagetter/BaGetter/issues/257). Apply policies at download *and* discovery endpoints with explicit handling for already cached versions.
- **Deployment:** Non-root container, least-privilege storage and secrets, pinned images, trust-configured reverse proxy, max upload/asset sizes, health/readiness, documented resource limits, backup and *tested restore* of database, files and symbol storage.
- **Release engineering:** Fix the broken or misleading Helm workflow, review [#249](https://github.com/bagetter/BaGetter/pull/249), generate changelogs/SBOM, scan dependencies and container images, and cut a tagged prerelease before recommending production usage.
- **Operational incident tests:** Database unavailable, storage full, upstream unavailable/slow, bad TLS, 429/rate limits, crash during staging/indexing, restored backup with missing file, proxy subpath, and two-replica race.

## G5: optional enterprise and ecosystem features

- **Feed tenancy and authentication:** Explore [#278](https://github.com/bagetter/BaGetter/pull/278) incrementally (local accounts/PATs, then Entra SSO, then isolated feeds/RBAC with migrations). Retain the fork's fail-closed publishing and HTML/security protections.
- **More upstreams:** Extract multi-upstream fallback from [#269](https://github.com/bagetter/BaGetter/pull/269) only after the single nuget.org origin is reliable. Require deterministic source priority, per-origin credentials, provenance, failure handling, and protection against same-ID/version hash conflicts.
- **Storage providers:** Evaluate S3-compatible ForcePathStyle ([#280](https://github.com/bagetter/BaGetter/pull/280)) and additional S3 settings ([#295](https://github.com/bagetter/BaGetter/pull/295)), as well as Azure managed identities ([#272](https://github.com/bagetter/BaGetter/pull/272)). Do not equate an implemented provider with proven operational support.
- **Polish:** Windows service support [#263](https://github.com/bagetter/BaGetter/pull/263); better symbols [#212](https://github.com/bagetter/BaGetter/pull/212); license/readme UX [#76](https://github.com/bagetter/BaGetter/pull/76) / [#121](https://github.com/bagetter/BaGetter/pull/121); search/UI fixes only after core NuGet behavior passes.

## Upstream PR triage (all 23 open PRs at review time)

| Upstream PRs | Decision for this fork | Gate |
| --- | --- | --- |
| [#290](https://github.com/bagetter/BaGetter/pull/290), [#288](https://github.com/bagetter/BaGetter/pull/288), [#294](https://github.com/bagetter/BaGetter/pull/294) | **Reconcile**, not wholesale cherry-pick: fork already covers core race/retention fixes; add missing scenarios and tests | G1 |
| [#285](https://github.com/bagetter/BaGetter/pull/285) | **High priority; split:** upstream search, metadata, safe full proxy, SSRF review | G3 |
| [#283](https://github.com/bagetter/BaGetter/pull/283) | **Adopt selectively:** ID/version policies with mirror provenance and cache checks | G2/G4 |
| [#269](https://github.com/bagetter/BaGetter/pull/269), [#292](https://github.com/bagetter/BaGetter/pull/292), [#278](https://github.com/bagetter/BaGetter/pull/278) | **Do not merge whole PRs:** overlapping .NET 10 upgrades; split platform, mirrors, auth and multi-feed | G0/G5 |
| [#282](https://github.com/bagetter/BaGetter/pull/282) | **Review** upstream provenance/UI without breaking existing service contracts | G2 |
| [#265](https://github.com/bagetter/BaGetter/pull/265) | **Adapt** for deprecation metadata and controlled authorization | G3 |
| [#284](https://github.com/bagetter/BaGetter/pull/284), [#177](https://github.com/bagetter/BaGetter/pull/177) | **Small UX patches:** safe icon fallback; validate templating, no priority over mirror | G3/G5 |
| [#244](https://github.com/bagetter/BaGetter/pull/244) | **Review** proxy URL generation alongside trusted-forwarded-header policy | G1/G4 |
| [#176](https://github.com/bagetter/BaGetter/pull/176), [#249](https://github.com/bagetter/BaGetter/pull/249) | **Consider** observability and Helm after security/CI review | G4 |
| [#280](https://github.com/bagetter/BaGetter/pull/280), [#295](https://github.com/bagetter/BaGetter/pull/295), [#272](https://github.com/bagetter/BaGetter/pull/272) | **Provider-specific** improvements, gated by actual usage and integration tests | G5 |
| [#263](https://github.com/bagetter/BaGetter/pull/263), [#212](https://github.com/bagetter/BaGetter/pull/212) | **Optional** Windows services and full-debug symbols | G5 |
| [#86](https://github.com/bagetter/BaGetter/pull/86), [#76](https://github.com/bagetter/BaGetter/pull/76), [#121](https://github.com/bagetter/BaGetter/pull/121) | **Defer/reassess** value and overlap with existing JSON, embedded-file and readme support | G5 |

**Integration rule:** Compare each candidate against the fork's post-hardening source, inspect dependency/license/migration implications, extract the minimal change, add relevant tests, and only merge when its gate passes. A PR marked open upstream is not implemented locally.

## Minimum CI and acceptance matrix

| Scenario | Required assertion |
| --- | --- |
| Cold: empty server cache, package present at nuget.org | Restore succeeds with only the BaGetter feed configured; byte-identical package |
| Warm: client cache cleared, server cache retained | Restore succeeds; no additional upstream .nupkg fetch for cached ID/version |
| Offline: all dependencies already cached, upstream blocked | Restore succeeds and cached package bytes remain unchanged |
| Concurrent cold requests, one BaGetter instance | 32/32 successful for existing version; no false 404/500; single origin download per ID/version |
| Two BaGetter replicas | Explicitly supported tested coordination or documented unsupported configuration |
| Unknown package/version at a healthy upstream | Proper 404, no permanently poisoned cache |
| Upstream timeout, 429, 5xx, bad DNS/TLS | Not misrepresented as a permanent package miss; bounded retry and clear diagnostics |
| Disk-full, crash, incomplete package | No partially published package; recovery is possible |
| Retention enabled during fetch of an old version | Requested package remains downloadable through the ongoing request |
| SemVer2/prerelease/normalized case and manifest | Versions, package content and registration responses are consistent |
| Reverse-proxy HTTPS/subpath and private credentials | Correct absolute URLs and authorization; no accidental public data disclosure |
| Optional full-proxy mode and UI | Upstream search/autocomplete function; security checks on rewritten assets; NuGet clients need only the proxy for core operations |
| Regression across OS/provider matrix | Linux + Windows builds/tests green; SQLite/PostgreSQL, real nuget.org smoke separately reported |

### Definition of done for the **first mirror-ready prerelease**

- G0's supported-runtime/CI requirements and all G1 test scenarios completed, with logs/artifacts retained.
- No known reproducible false-404, cache poisoning or missing-on-disk 500 in the supported deployment topology.
- Explicitly documented read-through vs full-proxy behavior, offline guarantees, supported providers/topologies and configuration examples.
- Production promotion requires a restore exercise, security review and monitored rollout. A green mock-upstream unit-test suite alone is not production acceptance.

## Suggested first implementation PRs (in order)

1. **test(mirror): live nuget.org smoke + local V3 fake + pinned dotnet restore** (capture baseline before changing behavior).
2. **fix(mirror): cache-entry integrity and recovery of missing package bytes** (issue #211).
3. **fix(mirror): distinguish upstream 404 from transient origin failures; cancellation and retry policy**.
4. **perf(mirror): metadata freshness/TTL and local-first upstream request coalescing** (issue #267).
5. **test(mirror): concurrent HTTP restores, offline cache and PostgreSQL matrix**.
6. **feat(proxy): upstream search and autocomplete with bounded caching** (first small extraction from PR #285).
7. **feat(proxy): secure asset rewriting and modern NuGet metadata/vulnerability resources** (later #285 work).
8. **chore(platform): standalone .NET 10 LTS upgrade**, worked in parallel with steps 1–5, not postponed behind full proxy.

## Operational usage until G1 is complete

- Set Mirror.Enabled=true and Mirror.PackageSource=https://api.nuget.org/v3/index.json **only in a controlled environment**. The default is mirror disabled.
- Do not advertise a real nuget.org mirror as a complete offline or transparent proxy until the relevant acceptance tests pass.
- For a single mirror-only NuGet.Config, use one source pointing to the BaGetter V3 service index. If mixing public/private sources, configure NuGet Package Source Mapping for direct *and transitive* dependencies to avoid ambiguous source selection.
- Keep automatic hard deletion/retention conservative until cache-vs-published-package semantics are defined. Back up database and blob storage together.
- Protect origin egress and credentials; do not trust arbitrary external asset URLs or redirects.

## Source documents

- [Open upstream pull requests](https://github.com/bagetter/BaGetter/pulls)
- [Open upstream issues](https://github.com/bagetter/BaGetter/issues)
- [Microsoft NuGet V3 protocol overview](https://learn.microsoft.com/en-us/nuget/api/overview)
- [Microsoft NuGet PackageBaseAddress specification](https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource)
- [Microsoft NuGet server implementation guide](https://learn.microsoft.com/en-us/nuget/api/implementation-guide)
- [NuGet Package Source Mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping)
- [.NET releases and support policy](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)

Update this roadmap after each merged implementation PR, newly reported mirror regression, upstream API change, or completed release gate.
