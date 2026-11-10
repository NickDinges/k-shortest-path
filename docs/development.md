# Project layout and tests

| Folder / file | Content |
|---|---|
| `csharp/Eppstein/` | The library (NuGet package `Eppstein.KShortestPaths`) |
| `csharp/Eppstein/Graph.cs` | Storage, building the graph, vertices and edges |
| `csharp/Eppstein/Graph.Paths.cs` | Queries: Dijkstra, corridor, path enumeration |
| `csharp/Eppstein/Graph.ArcFlags.cs` | Optional arc-flags speed-up |
| `csharp/Eppstein/Graph.ContractionHierarchy.cs` | Optional contraction hierarchy with PHAST |
| `csharp/Eppstein/Graph.Import.cs` | CSV and DIMACS import |
| `csharp/Eppstein/Graph.Serialize.cs` | Graphviz (DOT) output |
| `csharp/Eppstein/Vertex.cs`, `Edge.cs`, `GraphPath.cs` | The public value types |
| `csharp/Eppstein/SidetrackHeap.cs` | Persistent heap of detours (Eppstein's path enumeration) |
| `csharp/Eppstein/Graph.SimplePaths.cs` | Simple paths: Yen's algorithm with Lawler's improvement and A* |
| `csharp/Eppstein.Demo/` | Demo program (`dotnet run --project csharp/Eppstein.Demo -- [k] [--dot file]`) |
| `csharp/Eppstein.Tests/` | Automated tests (xUnit) |
| `csharp/Eppstein.Benchmarks/` | Timing benchmark (see [Performance](performance.md)) |
| `testdata/golden/` | Graphs with expected paths, checked by the C# **and** the Go tests |
| `.github/workflows/ci.yml` | CI: build, tests and a benchmark comparison for every pull request |
| `docs/` | The documentation, its images, and [an assessment of contraction hierarchies for this library](research-ch-and-k-shortest-paths.md) (with measurements of the implementation) |
| `go/` | Go port of the library ([own README](../go/README.md)) |
| `rust/` | Rust port of the library ([own README](../rust/README.md)) |
| `paper/` | Paper (LaTeX) with the cross-language measurements and their scripts ([README](../paper/README.md)) |

```sh
dotnet build csharp/Eppstein.sln   # compile (warnings count as errors)
dotnet test csharp/Eppstein.sln    # run all tests
dotnet pack csharp/Eppstein/Eppstein.csproj -c Release -o artifacts   # build the NuGet package
```

The tests compare the results with a brute-force search on hundreds of random graphs (with and
without cycles, corridors, arc flags and the contraction hierarchy; all golden cases also run with
the contraction hierarchy), and check the shared golden data, the Graphviz output
and the error handling. On top of that:

- **Property-based tests** (`csharp/Eppstein.Tests/PropertyTests.cs`, with
  [FsCheck](https://fscheck.github.io/FsCheck/)) generate thousands of graphs and import files per
  run, and shrink a failing input to a minimal example.
- **Fuzz tests** for the Go version (`go/eppstein/fuzz_test.go`) search for inputs that crash the
  imports or give wrong paths. Inputs they found are kept in `go/eppstein/testdata/fuzz` and run
  with every `go test`.

The CI (`.github/workflows/`) checks every pull request:

| Check | What it does |
|---|---|
| C# build and tests | Build without warnings, tests on .NET 8 and .NET 10, line coverage at least 95% (report in the job summary), NuGet package |
| Go vet and tests | Go 1.23, oldstable and stable: `gofmt`, `go vet`, `staticcheck`, tests with the race detector, coverage at least 93% |
| Go fuzzing | Each fuzz target for 1 minute (weekly for 10 minutes) |
| Benchmark comparison | Benchmarks of the base branch and of the pull request on the same machine |
| CodeQL | Static security analysis of the C# and Go code (also weekly) |

Measure the coverage yourself:

```sh
dotnet test csharp/Eppstein.sln --collect:"XPlat Code Coverage"
```

---

[Back to the main README](../README.md)
