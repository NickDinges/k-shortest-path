# Paper

*From a 2010 Prototype to Contraction Hierarchies in Three Languages: Engineering k Shortest Paths
with Eppstein, Yen–Lawler, Arc Flags and PHAST — an experiment in AI-assisted algorithm engineering.*

[main.pdf](main.pdf) is the compiled paper; `main.tex` and `references.bib` are its source. All
tables and numbers in the results come from the CSV files in `data/`, through
`scripts/make_tables.py` (which writes `tables/`).

## Reproduce

From the repository root (needs .NET 10, Go 1.23+, Rust and Python 3):

```sh
python3 paper/scripts/run_benchmarks.py --repeat 5        # C#, Go and Rust on identical graphs -> data/results.csv
python3 paper/scripts/run_runtime_experiments.py           # GC off, JIT settings -> data/runtime.csv
(cd paper/baseline && dotnet run -c Release -- 12 > ../data/baseline.csv)   # the 2010 version vs. today
python3 paper/scripts/make_tables.py                       # data/*.csv -> tables/*.tex
cd paper && latexmk -pdf main.tex                          # needs TeX Live with pgfplots
```

`run_benchmarks.py` stops if the three languages report different results. `data/environment.txt`
records the machine and compiler versions of the published numbers; `data/process.txt` the
development statistics used in section 7.

| Folder | Content |
|---|---|
| `data/` | Measurements (CSV) and environment |
| `tables/` | Generated tables (`*.tex` except `text-*.tex`) and the hand-written result texts (`text-*.tex`) |
| `scripts/` | Measurement and table scripts |
| `baseline/` | The original 2010 implementation (`v1/`, unchanged except the namespace) and a timing harness |
