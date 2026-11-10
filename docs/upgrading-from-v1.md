# Upgrading from version 1

Version 2 changed the interface. What to change in your code:

| Version 1 | Version 2 |
|---|---|
| Project `Eppstein2` (a program) | Library `Eppstein` (NuGet `Eppstein.KShortestPaths`); demo in `Eppstein.Demo` |
| `Path` | `GraphPath` (read-only; `Weight` is stored) |
| `edge.Weight = 5;` | `g.SetEdgeWeight(edge, 5);` |
| `vertex.RelatedEdges` | `vertex.OutEdges` and `vertex.InEdges` |
| `vertex.Object` | `vertex.Tag` |
| `vertex.Distance = …`, `vertex.EdgeToPath`, `vertex.Region` set by hand | read-only: `Distance`, `TreeEdge`, `Region` |
| `int w = path.Weight;` | `long w = path.Weight;` |
| `bool ok = g.AddEdge(…)` | `int id = g.AddEdge(…)` (edge number, `-1` on error) |
| `g.AddVertex(null)` returned `-1` | throws `ArgumentNullException` |
| `vertex.Equals("A")` (vertex vs. string) | `vertex.Label == "A"` |
| `SP_Node`, `ST_Node`, `PriorityQueue<T>` | removed (internal details) |
| loop with `FindNextShortestPath` | still works; `EnumeratePaths` is shorter |

---

[Back to the main README](../README.md)
