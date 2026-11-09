# Golden test data

Graphs and the expected paths between two of their vertices, shared by the C# tests
(`GoldenTests`) and the Go tests (`golden_test.go`), so both implementations are checked against
the same results.

- `cases.json`: one entry per query. `graph` names a file in this folder (`.csv` for
  `ImportEdges`, `.gr` for `ImportDimacs`), `from`/`to` are vertex labels, `maxDetour` /
  `maxDetourRatio` an optional corridor.
- `complete: true`: exactly these paths exist (then an empty path follows).
  `complete: false`: these are all paths up to the weight of the last one; more paths follow.
- Paths of equal weight may come in any order; the tests compare them as sets.

The expected paths were generated with the implementation of commit `a39d887` (C# and Go agreed on
all of them; both were also checked against brute-force enumeration). If a change makes a golden
test fail, the change is wrong, not the data, unless the meaning of the algorithm changes on
purpose.
