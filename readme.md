# BaGetter — Cenn0 fork

A self-hosted NuGet V3 package and symbol server written in C# and ASP.NET Core. This repository is an independently maintained fork of [bagetter/BaGetter](https://github.com/bagetter/BaGetter), itself derived from [BaGet](https://github.com/loic-sharma/BaGet).

> **Fork status:** Development is ongoing. In particular, comprehensive nuget.org mirror qualification, recovery and multi-instance guarantees are **not** yet established. See the [roadmap](ROADMAP.md) and [mirror operations guide](docs/MIRROR.md). The upstream project's documentation, container images and releases do not automatically describe or contain changes made in this fork.

## Quick start

### From source

Install the .NET SDK matching [global.json](global.json) (.NET 10 feature band or an allowed compatible roll-forward), then run:

```bash
git clone https://github.com/Cenn0/BaGetter.git
cd BaGetter
dotnet restore
dotnet run --project src/BaGetter --urls http://localhost:5000
```

Open `http://localhost:5000/v3/index.json` to verify the NuGet V3 service index. The web UI is available at `http://localhost:5000/`. The default SQLite database and local filesystem storage are suitable for evaluation, not a documented highly available production topology.

### Docker

The fork contains a [Dockerfile](Dockerfile); build an image from **this repository** to run fork-specific code:

```bash
docker build -t bagetter-local:dev .
docker volume create bagetter-data
docker run --rm -p 127.0.0.1:5000:8080 -v bagetter-data:/data --name bagetter bagetter-local:dev
```

Use `http://localhost:5000/v3/index.json` as the feed URL. The image sets its SQLite database, packages and symbols under `/data`. Persist that volume across upgrades. The local-only host binding in this example is intentional; for external access use an authenticated TLS reverse proxy and a reviewed network/security configuration.

**Important:** `bagetter/bagetter:latest` on Docker Hub belongs to the upstream distribution and should not be assumed to include this fork's changes. Likewise, [upstream releases](https://github.com/bagetter/BaGetter/releases) do not build this fork.

## Core capabilities

- NuGet V3 feed for serving packages, with a web interface and symbol-serving capabilities.
- Configurable storage and database providers (review provider-specific deployment requirements before production use).
- Optional upstream package read-through caching via the `Mirror` configuration.
- Development and regression testing with .NET 10, including dedicated mirror smoke-test infrastructure.

**Mirror scope:** A read-through cache is **not** a transparent full NuGet V3 proxy. Upstream search/autocomplete, metadata routing, offline behavior for uncached packages, and HA/multi-replica correctness require separate verification. See [mirror guide](docs/MIRROR.md).

## Documentation

| Document | Audience | Purpose |
| --- | --- | --- |
| [Deployment and configuration](docs/OPERATIONS.md) | Operators | Setup, persistent storage, configuration, secure exposure, backup and updates |
| [NuGet.org mirror](docs/MIRROR.md) | Operators and developers | Read-through mode, known limitations and validation |
| [Contributing](CONTRIBUTING.md) | Contributors | Build, test and contribution workflow |
| [Roadmap](ROADMAP.md) | All | Planned changes, milestones and release gates |
| [Upstream documentation](https://www.bagetter.com/) | Reference only | General BaGetter concepts; verify against this fork before applying |

## Development

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

GitHub Actions runs the regular build/test matrix on Linux and Windows. A separate real-nuget.org mirror smoke workflow exists; its results are not implied by the regular test status.

[View this fork's CI](https://github.com/Cenn0/BaGetter/actions) · [Issues](https://github.com/Cenn0/BaGetter/issues) · [Pull requests](https://github.com/Cenn0/BaGetter/pulls)

## Security and support

Do not expose a writable package feed to untrusted networks without authentication, authorization, HTTPS, request limits and a backup/restore procedure. An empty `ApiKey` in the default configuration is **not** a production security setup. Review actual authorization behavior and your deployment before enabling publishing. Report security-sensitive findings privately where feasible; avoid posting credentials, tokens or exploitable deployment details in public issues.

This fork is community-maintained; it does not imply commercial support, upstream endorsement, production certification or a fixed release schedule.

## License and attribution

Distributed under the [MIT license](LICENSE). Original project and contributor attribution remain with [BaGetter](https://github.com/bagetter/BaGetter) and [BaGet](https://github.com/loic-sharma/BaGet).
