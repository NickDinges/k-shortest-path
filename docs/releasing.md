# Releasing

One tag publishes all three libraries with the same version number.

| Language | Package | Where it is published |
|---|---|---|
| C# | `Eppstein.KShortestPaths` | [nuget.org](https://www.nuget.org/packages/Eppstein.KShortestPaths) |
| Rust | `eppstein` | [crates.io](https://crates.io/crates/eppstein) |
| Go | `github.com/nickdinges/k-shortest-path/go/v2` | the tag `go/vX.Y.Z` in this repository (Go has no registry to upload to) |

## One-time setup

1. Create an API key on nuget.org (scope: push new packages and versions) and store it as the
   repository secret `NUGET_API_KEY`.
2. Create an API token on crates.io (scope: publish new crates and updates) and store it as the
   repository secret `CARGO_REGISTRY_TOKEN`.

Secrets are added under *Settings → Secrets and variables → Actions*. If a secret is missing, the
release workflow skips that registry and does everything else.

## Publishing a version

1. Set the new version in `csharp/Eppstein/Eppstein.csproj` (`<Version>`), `rust/Cargo.toml`
   (`version`) and `CITATION.cff` (`version`). For a new major version, also change the `/v2` at
   the end of the module path in `go/go.mod` and in the Go imports.
2. Check that they agree:

   ```sh
   .github/scripts/check-version.sh 2.0.0
   ```

3. Merge to the default branch, then tag that commit and push the tag:

   ```sh
   git tag v2.0.0
   ```

   ```sh
   git push origin v2.0.0
   ```

The workflow [`release.yml`](../.github/workflows/release.yml) then checks the versions, runs the
tests of all three languages, pushes the NuGet package and the crate, creates the tag `go/v2.0.0`
for the Go module, and creates a GitHub release with the NuGet package and the paper attached.

A published version cannot be replaced: nuget.org and crates.io accept each version number once,
and the Go module proxy keeps what it has seen. To fix a mistake, publish a new version.

## Citing

[`CITATION.cff`](../CITATION.cff) describes the software and the paper; GitHub shows it as "Cite
this repository". For a permanent identifier, connect the repository to
[Zenodo](https://zenodo.org/): it then assigns a DOI to every GitHub release.
