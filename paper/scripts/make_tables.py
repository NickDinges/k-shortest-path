#!/usr/bin/env python3
"""Generates the LaTeX tables and number macros of the paper from paper/data/*.csv.

Usage (from the repository root): python3 paper/scripts/make_tables.py
"""
import csv
import os
import re
import subprocess

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DATA = os.path.join(ROOT, "paper", "data")
OUT = os.path.join(ROOT, "paper", "tables")
LANGS = ["C#", "Go", "Rust"]
TEX = {"C#": r"\cs{}", "Go": "Go", "Rust": "Rust"}

rows = {}
with open(os.path.join(DATA, "results.csv")) as f:
    for r in csv.DictReader(f):
        rows[(r["scenario"], r["language"])] = r


def val(scenario, lang, key):
    v = rows[(scenario, lang)][key]
    return float(v) if v != "" else None


def ms(x):
    """Milliseconds with sensible precision."""
    if x is None:
        return "--"
    if x >= 10000:
        return f"\\num{{{x / 1000:.1f}}}\\,s"
    if x >= 100:
        return f"\\num{{{x:.0f}}}"
    if x >= 10:
        return f"{x:.1f}"
    return f"{x:.2f}" if x < 1 else f"{x:.1f}"


def best(values):
    """Bold the smallest value (within 3%) in a row."""
    m = min(v for v in values if v is not None)
    return [r"\textbf{" + ms(v) + "}" if v is not None and v <= m * 1.03 else ms(v) for v in values]


def write(name, text):
    with open(os.path.join(OUT, name), "w") as f:
        f.write(text)


def table(name, caption, label, header, body, spec):
    write(name, "\\begin{table}[t]\n\\centering\n\\caption{" + caption + "}\n\\label{" + label + "}\n\\small\n"
          "\\begin{tabular}{" + spec + "}\n\\toprule\n" + header + " \\\\\n\\midrule\n" + body +
          "\\bottomrule\n\\end{tabular}\n\\end{table}\n")


def lang_header(groups):
    cells = " & ".join(f"\\multicolumn{{3}}{{c}}{{{g}}}" for g in groups)
    rules = "".join(f"\\cmidrule(lr){{{2 + 3 * i}-{4 + 3 * i}}}" for i in range(len(groups)))
    return " & " + cells + " \\\\ " + rules + "\n" + " & ".join([""] + [TEX[l] for l in LANGS] * len(groups))


def rng(scenario, key, langs=LANGS):
    """Smallest and largest value over the languages, as a range for the text."""
    values = [val(scenario, l, key) for l in langs]
    return plain(min(values)) + "--" + plain(max(values))


def plain(x):
    """A number for the running text (no siunitx)."""
    if x >= 100:
        return f"{x:,.0f}".replace(",", "{,}")
    if x >= 10:
        return f"{x:.1f}".rstrip("0").rstrip(".") if x == round(x) else f"{x:.1f}"
    return f"{x:.2f}" if x < 1 else f"{x:.1f}"


TREE, NEXT = "warm_median_ms", "next_ms"

# Eppstein corner to corner
body = ""
for sc, label in [("eppstein-200", r"$200^2$, $k=10^4$"), ("eppstein-500", r"$500^2$, $k=10^4$"),
                  ("eppstein-1000", r"$1000^2$, $k=10^4$"), ("eppstein-1000-k100k", r"$1000^2$, $k=10^5$")]:
    body += label + " & " + " & ".join(best([val(sc, l, TREE) for l in LANGS]) + best(
        [val(sc, l, NEXT) for l in LANGS])) + " \\\\\n"
table("eppstein.tex", "Eppstein, corner to corner on one-way grids (ms). Tree: steady-state query; next: the "
      "remaining $k-1$ paths, written out as edge lists. Fastest per group in bold (within 3\\%).",
      "tab:eppstein", lang_header(["tree (steady state)", "next paths"]), body, "l" + "rrr" * 2)

# Enumeration: paths vs. weights only, allocation and garbage collection
body = ""
for sc, label in [("eppstein-1000-k100k", "paths written out (ms)"), ("weights-1000-k100k", "weights only (ms)")]:
    body += label + " & " + " & ".join(best([val(sc, l, NEXT) for l in LANGS])) + " \\\\\n"
body += "\\midrule\n"
for key, label, fmt in [("alloc_mb", "allocated, paths written out (MB)", "{:,.0f}"),
                        ("gc_count", "garbage collections", "{:.0f}"), ("gc_pause_ms", "total GC pause (ms)", "{:.1f}")]:
    cells = [fmt.format(val("eppstein-1000-k100k", l, key)).replace(",", "{,}") for l in LANGS]
    if key != "alloc_mb":
        cells[2] = "--"
    body += label + " & " + " & ".join(cells) + " \\\\\n"
body += "allocated, weights only (MB) & " + " & ".join(
    f"{val('weights-1000-k100k', l, 'alloc_mb'):.0f}" for l in LANGS) + " \\\\\n"
table("enumeration.tex", "Enumerating $10^5$ paths on the $10^6$-vertex grid after the tree is known: time with "
      "and without writing out the paths, and the memory allocated and garbage collections during the "
      "enumeration with paths.", "tab:enumeration", " & " + " & ".join(TEX[l] for l in LANGS), body, "lrrr")

# Corridor
body = ""
for sc, label in [("local-1000", "no corridor"), ("local-1000-ratio", r"\texttt{MaxDetourRatio} $=0.1$")]:
    body += label + " & " + " & ".join(best([val(sc, l, "first_ms") for l in LANGS]) + best(
        [val(sc, l, TREE) for l in LANGS]) + best([val(sc, l, NEXT) for l in LANGS])) + " \\\\\n"
table("local.tex", "Nearby vertices in the middle of the $10^6$-vertex grid, $k=10^4$ (ms). First: first query of "
      "the process (includes one-time work such as JIT compilation and building adjacency rows); tree: "
      "steady state.", "tab:local", lang_header(["first query", "tree (steady state)", "next paths"]), body,
      "l" + "rrr" * 3)

# Building and memory
body = ""
for sc, label in [("eppstein-1000", "by index"), ("build-strings-1000", "by label strings"),
                  ("build-dimacs-1000", "DIMACS text import")]:
    body += label + " & " + " & ".join(best([val(sc, l, "build_ms") for l in LANGS])) + " \\\\\n"
body += r"\midrule" + "\nlive memory of the graph (MB) & " + " & ".join(
    f"{val('eppstein-1000', l, 'memory_mb'):.0f}" for l in LANGS) + " \\\\\n"
body += "peak resident memory of the process (MB) & " + " & ".join(
    f"{val('eppstein-1000', l, 'peak_rss_mb'):.0f}" for l in LANGS) + " \\\\\n"
table("build.tex", "Building the $10^6$-vertex grid ($2.1\\cdot10^6$ edges), in ms; live memory of the graph "
      "with one query and peak resident memory of the whole benchmark process (built by index, $k=10^4$).",
      "tab:build", " & " + " & ".join(TEX[l] for l in LANGS), body, "lrrr")

# Yen
body = ""
for sc, label in [("yen-200-k100", r"$200^2$, $k=100$"), ("yen-200-k1000", r"$200^2$, $k=1000$"),
                  ("yen-local-1000-k100", r"$1000^2$ local, ratio 0.1, $k=100$")]:
    body += label + " & " + " & ".join(best([val(sc, l, NEXT) for l in LANGS])) + " \\\\\n"
table("yen.tex", "Simple paths (Yen--Lawler with A*) on two-way grids: time for the paths after the first (ms).",
      "tab:yen", " & " + " & ".join(TEX[l] for l in LANGS), body, "lrrr")

# Arc flags and contraction hierarchy: preprocessing and steady-state tree
body = ""
for sc, label, key in [("plain-200-two-way", r"$200^2$ two-way: Dijkstra", None),
                       ("arcflags-4-200-two-way", r"\quad arc flags, 4 regions", "arcflags_ms"),
                       ("arcflags-16-200-two-way", r"\quad arc flags, 16 regions", "arcflags_ms"),
                       ("ch-200-two-way", r"\quad CH + PHAST", "contraction_ms"),
                       ("eppstein-1000", r"$1000^2$ one-way: Dijkstra", None),
                       ("ch-1000", r"\quad CH + PHAST", "contraction_ms")]:
    if key is None and body:
        body += "\\midrule\n"
    pre = best([val(sc, l, key) for l in LANGS]) if key else ["--"] * 3
    shortcuts = f"{int(float(rows[(sc, 'Rust')]['shortcuts'])):,}".replace(",", "{,}") if key == "contraction_ms" \
        else "--"
    body += label + " & " + " & ".join(pre + best([val(sc, l, TREE) for l in LANGS])) + " & " + shortcuts + " \\\\\n"
table("ch.tex", "Preprocessing techniques: preprocessing time and the steady-state shortest-path tree, in ms, and "
      "the number of shortcuts (Rust; \\cs{} differs slightly because of tie-breaking). The 16-region arc "
      "flags and the $1000^2$ contraction were run in one process per language.", "tab:ch",
      lang_header(["preprocessing", "tree (steady state)"]) + " & shortcuts", body, "l" + "rrr" * 2 + "r")

# Runtime sensitivity (GC and JIT)
runtime = {}
with open(os.path.join(DATA, "runtime.csv")) as f:
    for r in csv.DictReader(f):
        runtime[(r["scenario"], r["language"], r["setting"])] = r
RUNTIME_SCENARIOS = ["eppstein-200", "eppstein-1000-k100k", "yen-200-k1000"]
body = ""
for lang, setting, label in [("C#", "default", r"\cs{} default"), ("C#", "gen0 256 MB", r"\cs{} gen0 budget 256\,MB"),
                             ("C#", "non-concurrent GC", r"\cs{} non-concurrent GC"),
                             ("C#", "no tiered JIT", r"\cs{} without tiered JIT"), ("Go", "default", "Go default"),
                             ("Go", "GOGC=off", r"Go \texttt{GOGC=off}"), ("Rust", "default", "Rust")]:
    cells = []
    for sc in RUNTIME_SCENARIOS:
        r = runtime[(sc, lang, setting)]
        gc = "--" if lang == "Rust" else f"{float(r['gc_count']):.0f}"
        cells += [ms(float(r["repeated_ms"])), ms(float(r["warm_median_ms"])), ms(float(r["next_ms"])), gc]
    body += label + " & " + " & ".join(cells) + " \\\\\n"
table("runtime.tex", "Runtime settings (median ms). 2nd: second query of the process; steady: median of 20 "
      "further queries; next: the remaining paths; GCs: garbage collections while the next paths are "
      "enumerated.", "tab:runtime",
      " & \\multicolumn{4}{c}{Eppstein $200^2$, $k=10^4$} & \\multicolumn{4}{c}{Eppstein $1000^2$, $k=10^5$} & "
      "\\multicolumn{4}{c}{Yen $200^2$, $k=10^3$} \\\\ \\cmidrule(lr){2-5}\\cmidrule(lr){6-9}"
      "\\cmidrule(lr){10-13}\n" + " & 2nd & steady & next & GCs" * 3, body, "@{}l" + "rrrr" * 3 + "@{}")

# Values for the running text: \val{scenario/language/field} and named macros
values = {}
SHORT = {"C#": "cs", "Go": "go", "Rust": "rust"}
KEYS = {"build_ms": "build", "memory_mb": "mem", "first_ms": "first", "repeated_ms": "second", "next_ms": "next",
        "warm_median_ms": "warm", "contraction_ms": "contraction", "gc_count": "gc", "gc_pause_ms": "pause",
        "alloc_mb": "alloc", "arcflags_ms": "arcflags", "peak_rss_mb": "rss"}
for (sc, lang), r in rows.items():
    for key, short in KEYS.items():
        if r[key] != "":
            values[f"{sc}/{SHORT[lang]}/{short}"] = plain(float(r[key]))
            if key.endswith("_ms") and float(r[key]) >= 1000:
                values[f"{sc}/{SHORT[lang]}/{short}:s"] = plain(float(r[key]) / 1000)
    if r["shortcuts"] != "":
        values[f"{sc}/{SHORT[lang]}/shortcuts"] = f"{int(float(r['shortcuts'])):,}".replace(",", "{,}")
for sc in sorted({k[0] for k in rows}):
    for key, short in KEYS.items():
        if all(rows[(sc, l)][key] != "" for l in LANGS):
            values[f"{sc}/range/{short}"] = rng(sc, key)
            if key.endswith("_ms") and min(val(sc, l, key) for l in LANGS) >= 1000:
                values[f"{sc}/range/{short}:s"] = plain(min(val(sc, l, key) for l in LANGS) / 1000) + "--" + \
                    plain(max(val(sc, l, key) for l in LANGS) / 1000)
for (sc, lang, setting), r in runtime.items():
    for key, short in KEYS.items():
        if key in r and r[key] != "":
            values[f"rt/{sc}/{SHORT[lang]}/{setting.replace(' ', '-')}/{short}"] = plain(float(r[key]))

macros = {}
big = ["eppstein-1000", "eppstein-1000-k100k", "local-1000"]  # the 10^6-vertex graphs, full tree
ratio = lambda sc, key: max(val(sc, l, key) for l in LANGS) / min(val(sc, l, key) for l in LANGS)
macros["NumMaxRatioTreeBig"] = f"{max(ratio(sc, TREE) for sc in big):.1f}"
all_time = [sc for sc in sorted({k[0] for k in rows})]
macros["NumMaxRatioTreeAll"] = f"{max(ratio(sc, TREE) for sc in all_time):.1f}"
macros["NumMaxRatioTreeAllScenario"] = max(all_time, key=lambda sc: ratio(sc, TREE))
macros["NumMaxRatioSecond"] = f"{max(ratio(sc, 'repeated_ms') for sc in all_time):.1f}"
macros["NumMaxRatioNext"] = f"{max(ratio(sc, NEXT) for sc in all_time if val(sc, 'Rust', NEXT) > 0):.1f}"
macros["NumLocalRepeated"] = plain(val("local-1000", "Rust", TREE)) + "\\,ms"
macros["NumLocalRatioRepeated"] = plain(val("local-1000-ratio", "Rust", TREE)) + "\\,ms"
speedups = [val("local-1000", l, TREE) / val("local-1000-ratio", l, TREE) for l in LANGS]
macros["NumCorridorSpeedup"] = f"{min(speedups):.0f}--{max(speedups):.0f}"
speedups = [val("eppstein-1000", l, TREE) / val("ch-1000", l, TREE) for l in LANGS]
macros["NumPhastSpeedupBig"] = f"{min(speedups):.1f}--{max(speedups):.1f}"
speedups = [val("plain-200-two-way", l, TREE) / val("ch-200-two-way", l, TREE) for l in LANGS]
macros["NumPhastSpeedupSmall"] = f"{min(speedups):.1f}--{max(speedups):.1f}"
for regions in (4, 16):
    gains = [100 * (1 - val(f"arcflags-{regions}-200-two-way", l, TREE) / val("plain-200-two-way", l, TREE))
             for l in LANGS]
    macros["NumArcFlagsGain" + {4: "Four", 16: "Sixteen"}[regions]] = f"{min(gains):.0f}--{max(gains):.0f}"
per_path = [1000 * val("eppstein-1000-k100k", l, NEXT) / 1e5 for l in LANGS]
macros["NumPerPathMicros"] = f"{min(per_path):.0f}--{max(per_path):.0f}"
per_weight = [1000 * val("weights-1000-k100k", l, NEXT) / 1e5 for l in LANGS]
macros["NumPerWeightMicros"] = f"{min(per_weight):.1f}--{max(per_weight):.1f}"
shares = [100 * (1 - val("weights-1000-k100k", l, NEXT) / val("eppstein-1000-k100k", l, NEXT)) for l in LANGS]
macros["NumOutputShare"] = f"{min(shares):.0f}--{max(shares):.0f}"
cold = [val(sc, "C#", "repeated_ms") / val(sc, "C#", TREE) for sc in all_time]
macros["NumCsSecondOverSteadyMax"] = f"{max(cold):.1f}"
# Dispersion: (largest - smallest) / median of the per-process values, for entries measured in several processes
spreads = []
for (sc, lang), r in rows.items():
    if int(r["runs"]) > 1:
        for lo, hi, mid in [("warm_lo_ms", "warm_hi_ms", TREE), ("next_lo_ms", "next_hi_ms", NEXT)]:
            if float(r[mid]) >= 1:  # below 1 ms the timer resolution of the table dominates
                spreads.append((float(r[hi]) - float(r[lo])) / float(r[mid]))
spreads.sort()
macros["NumSpreadMedian"] = f"{100 * spreads[len(spreads) // 2]:.0f}"
macros["NumSpreadNinety"] = f"{100 * spreads[int(0.9 * (len(spreads) - 1))]:.0f}"
macros["NumSpreadMax"] = f"{100 * spreads[-1]:.0f}"
repeat = max(int(rows[k]["runs"]) for k in rows)
macros["NumRepeat"] = str(repeat)

# Baseline
baseline = os.path.join(DATA, "baseline.csv")
if os.path.exists(baseline):
    with open(baseline) as f:
        b = list(csv.DictReader(f))
    v1 = [r for r in b if r["version"] == "v1"]
    last = v1[-1]
    v2 = next(r for r in b if r["version"] == "v2" and r["size"] == last["size"])
    first = float(last["first_ms"])
    macros["NumVOneSize"] = last["size"]
    macros["NumVOneFirst"] = f"{first / 1000:.1f}\\,s" if first >= 1000 else f"{first:.0f}\\,ms"
    macros["NumVTwoFirst"] = f"{float(v2['first_ms']):.2f}\\,ms"
    # size 3 is the first call in the process (JIT warm-up), not algorithm time; sizes beyond the
    # largest one v1 finished were not measured for v1 (the run was stopped there)
    max_size = int(last["size"])
    coords = lambda version, key: " ".join(f"({r['size']},{max(float(r[key]), 0.001)})" for r in b
                                          if r["version"] == version and 4 <= int(r["size"]) <= max_size)
    write("baseline.tex", r"""\begin{figure}[t]
\centering
\begin{tikzpicture}
\begin{semilogyaxis}[width=0.8\textwidth,height=6cm,xlabel={grid size $n$ ($n\times n$ acyclic grid)},
  ylabel={time (ms)},legend pos=north west,grid=major,legend cell align=left]
\addplot+[mark=*,red] coordinates {""" + coords("v1", "first_ms") + r"""};
\addlegendentry{2010 original: first path}
\addplot+[mark=square*,blue] coordinates {""" + coords("v2", "first_ms") + r"""};
\addlegendentry{current \cs{}: first path}
\addplot+[mark=triangle*,blue,dashed] coordinates {""" + coords("v2", "next999_ms") + r"""};
\addlegendentry{current \cs{}: next 999 paths}
\end{semilogyaxis}
\end{tikzpicture}
\caption{The 2010 implementation enumerates all $\binom{2(n-1)}{n-1}$ paths in the first query; the current one
computes one tree and then each path on demand.}
\label{fig:baseline}
\end{figure}
""")
else:
    for k in ["NumVOneSize", "NumVOneFirst", "NumVTwoFirst"]:
        macros[k] = "?"
    write("baseline.tex", "")

# Code statistics
def loc(files):
    n = 0
    for path in files:
        for line in open(path, encoding="utf-8-sig"):
            s = line.strip()
            if s and not s.startswith(("//", "///", "*")):
                n += 1
    return n


def files(directory, pattern, exclude=None):
    result = []
    for name in sorted(os.listdir(os.path.join(ROOT, directory))):
        if re.fullmatch(pattern, name) and not (exclude and re.fullmatch(exclude, name)):
            result.append(os.path.join(ROOT, directory, name))
    return result


macros["NumLocCs"] = str(loc(files("csharp/Eppstein", r".*\.cs")))
macros["NumLocCsTests"] = str(loc(files("csharp/Eppstein.Tests", r".*\.cs")))
macros["NumLocGo"] = str(loc(files("go/eppstein", r".*\.go", r".*_test\.go")))
macros["NumLocGoTests"] = str(loc(files("go/eppstein", r".*_test\.go")))
macros["NumLocRust"] = str(loc(files("rust/src", r".*\.rs")))
macros["NumLocRustTests"] = str(loc(files("rust/tests", r".*\.rs")))

env = open(os.path.join(DATA, "environment.txt")).read()
cpu = re.search(r"Model name:\s*(.+)", env)
macros["NumCpu"] = cpu.group(1).strip().replace("(R)", "").replace("(TM)", "") if cpu else "a cloud VM"
macros["NumDotnet"] = re.search(r"^(\d+\.\d+\.\d+)", env, re.M).group(1)
macros["NumGo"] = re.search(r"go version (go[\d.]+)", env).group(1).replace("go", "Go~")
macros["NumRustc"] = re.search(r"rustc ([\d.]+)", env).group(1)

process = os.path.join(DATA, "process.txt")
for line in open(process):
    key, value = line.rstrip("\n").split("=", 1)
    macros[key] = value

write("numbers.tex", "".join(f"\\newcommand{{\\{k}}}{{{v}}}\n" for k, v in sorted(macros.items())) +
      "".join(f"\\expandafter\\def\\csname val@{k}\\endcsname{{{v}}}\n" for k, v in sorted(values.items())))
print("\n".join(f"{k} = {v}" for k, v in sorted(macros.items())))
