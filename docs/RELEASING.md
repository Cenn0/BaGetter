# Releasing this BaGetter fork

Use Actions → **Release (BaGetter fork)** → Run workflow on `main` with a new Semantic Versioning tag, for example `v1.0.0-rc.1` or `v1.0.0`. You may also push an existing `vX.Y.Z` tag on an already integrated `main` commit. Do **not** use a tag from an unmerged branch.

The workflow validates the tag and source ancestry, compiles and tests the full .NET solution, publishes versioned Linux amd64/arm64 OCI images to `ghcr.io/cenn0/bagetter:<version>`, then publishes a framework-dependent .NET application ZIP with `SHA256SUMS.txt` to [GitHub Releases](https://github.com/Cenn0/BaGetter/releases). If image publishing fails, the GitHub Release is not created.

The ZIP needs a compatible .NET/ASP.NET runtime on the target machine. OCI images are independently built from the release source SHA; they are **not byte-identical** to the ZIP. Verify GitHub release downloads with `sha256sum -c SHA256SUMS.txt`; review package/image availability and visibility in GHCR after the first release.

**Fork isolation:** This workflow intentionally does not publish upstream `BaGetter` NuGet package IDs, Docker Hub tags or upstream Helm charts. GitHub packages can initially have restricted visibility: explicitly inspect and set the GHCR package to public if intended. Never put private credentials or personal email addresses in public release notes.

The release job creates tags itself when manually dispatched. A `GITHUB_TOKEN`-created tag does not trigger a second tag-push workflow, so all stages execute in this workflow. Add repository tag protection and enable GitHub release immutability in Settings; YAML cannot enforce those repository settings.
