# Contributing to the Cenn0/BaGetter fork

Thanks for contributing. This is an independent fork of [BaGetter](https://github.com/bagetter/BaGetter); file issues and pull requests against [this repository](https://github.com/Cenn0/BaGetter) when your change targets the fork.

## Development environment

- .NET SDK as pinned by [global.json](global.json) (.NET 10).
- Git and an editor supporting C#.
- Optional: Docker for building and testing the deployment image.

```bash
git clone https://github.com/Cenn0/BaGetter.git
cd BaGetter
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet run --project src/BaGetter --urls http://localhost:5000
```

Check the service index at `http://localhost:5000/v3/index.json`. Run tests again after making code changes. When changing database or storage integration, add regression coverage for failure and cancellation paths.

## Pull request expectations

1. Open a focused PR targeting `main`; explain the problem, implementation and any compatibility risks.
2. Add or update tests for behavioral changes. Include exact reproduction and verification instructions, especially for NuGet client behavior.
3. Update the README, operation docs or roadmap where behavior, configuration or limitations change.
4. Keep credentials and local environment configuration out of commits. Do not introduce unaudited external code or alter dependency licenses without review.
5. Allow the Linux and Windows CI matrix to finish. For mirror changes, use the dedicated smoke suite where relevant; a passing unit-test matrix alone does not demonstrate nuget.org connectivity or offline recovery.

Read [ROADMAP.md](ROADMAP.md) before starting a larger feature. Upstream PRs are useful references, not automatically compatible patches. Coordinate large architectural changes through an issue first.

## Mirror-specific review checklist

- Correct HTTP classification: distinguish confirmed package absence from upstream outage.
- Validate package identity/version and bytes before making cache entries visible.
- Exercise cold/warm download, concurrent misses, restart and missing-file recovery.
- Preserve local package immutability and avoid overwriting locally published versions.
- Record limitations of single-process locking and non-atomic database/blob publication.

Follow the [code of conduct](CODE_OF_CONDUCT.md) and applicable license notices.
