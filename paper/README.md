# Paper

*From a 2010 Prototype to Contraction Hierarchies in Three Languages: Engineering k Shortest Paths
with Eppstein, Yen–Lawler, Arc Flags and PHAST — an experiment in AI-assisted algorithm engineering.*

[main.pdf](main.pdf) is the compiled paper; `main.tex` and `references.bib` are its source. All
tables come from the CSV files in `data/`, through `scripts/make_tables.py` (which writes
`tables/`). The measured numbers in the running text come from the same files: the script writes
them to `tables/numbers.tex`, and the text inserts them with `\val{scenario/language/field}`
(LaTeX stops with an error if a value does not exist), so text and tables cannot drift apart.

## Reproduce

From the repository root (needs .NET 10, Go 1.23+, Rust and Python 3):

```sh
python3 paper/scripts/run_benchmarks.py --repeat 5        # C#, Go and Rust on identical graphs -> data/results.csv (about 40 min)
python3 paper/scripts/run_runtime_experiments.py           # GC and JIT settings -> data/runtime.csv (about 15 min)
(cd paper/baseline && dotnet run -c Release -- 12 > ../data/baseline.csv)   # the 2010 version vs. today
python3 paper/scripts/make_tables.py                       # data/*.csv -> tables/*.tex
cd paper && latexmk -pdf main.tex                          # needs TeX Live with pgfplots
```

`run_benchmarks.py` stops if the three languages report different results. Every scenario runs in
5 fresh processes per language. In each process the query runs 22 times: the first (cold), the
second, and 20 more whose median is the steady-state time. The CSV holds the medians over the
processes, the smallest and largest steady-state and next-paths time, the garbage collections,
their pause time and the allocated bytes during the enumeration, and the peak resident memory. `data/environment.txt`
records the machine and compiler versions of the published numbers; `data/process.txt` the
development statistics used in section 7.

| Folder | Content |
|---|---|
| `data/` | Measurements (CSV) and environment |
| `tables/` | Generated tables and `numbers.tex` (all values used in the text) |
| `scripts/` | Measurement and table scripts |
| `baseline/` | The original 2010 implementation (`v1/`, unchanged except the namespace) and a timing harness |
