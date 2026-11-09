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


# Eppstein corner to corner
body = ""
for sc, label in [("eppstein-200", r"$200^2$, $k=10^4$"), ("eppstein-500", r"$500^2$, $k=10^4$"),
                  ("eppstein-1000", r"$1000^2$, $k=10^4$"), ("eppstein-1000-k100k", r"$1000^2$, $k=10^5$")]:
    body += label + " & " + " & ".join(best([val(sc, l, "repeated_ms") for l in LANGS]) + best(
        [val(sc, l, "next_ms") for l in LANGS])) + " \\\\\n"
table("eppstein.tex", "Eppstein, corner to corner on one-way grids (median times in ms). Tree: repeated query; "
      "next: the remaining $k-1$ paths. Fastest per group in bold (within 3\\%).", "tab:eppstein",
      lang_header(["tree", "next paths"]), body, "l" + "rrr" * 2)

# Corridor
body = ""
for sc, label in [("local-1000", "no corridor"), ("local-1000-ratio", r"\texttt{MaxDetourRatio} $=0.1$")]:
    body += label + " & " + " & ".join(best([val(sc, l, "first_ms") for l in LANGS]) + best(
        [val(sc, l, "repeated_ms") for l in LANGS]) + best([val(sc, l, "next_ms") for l in LANGS])) + " \\\\\n"
table("local.tex", "Nearby vertices in the middle of the $10^6$-vertex grid, $k=10^4$ (ms). First: first query "
      "(includes one-time work such as JIT compilation and building adjacency rows).", "tab:local",
      lang_header(["first query", "tree", "next paths"]), body, "l" + "rrr" * 3)

# Building and memory
body = ""
for sc, label in [("eppstein-1000", "by index"), ("build-strings-1000", "by label strings"),
                  ("build-dimacs-1000", "DIMACS text import")]:
    body += label + " & " + " & ".join(best([val(sc, l, "build_ms") for l in LANGS])) + " \\\\\n"
body += r"\midrule" + "\nlive memory (MB) & " + " & ".join(
    f"{val('eppstein-1000', l, 'memory_mb'):.0f}" for l in LANGS) + " \\\\\n"
table("build.tex", "Building the $10^6$-vertex grid ($2.1\\cdot10^6$ edges), in ms, and the live memory of the "
      "graph with one query (built by index).", "tab:build", " & " + " & ".join(TEX[l] for l in LANGS), body, "lrrr")

# Yen
body = ""
for sc, label in [("yen-200-k100", r"$200^2$, $k=100$"), ("yen-200-k1000", r"$200^2$, $k=1000$"),
                  ("yen-local-1000-k100", r"$1000^2$ local, ratio 0.1, $k=100$")]:
    body += label + " & " + " & ".join(best([val(sc, l, "next_ms") for l in LANGS])) + " \\\\\n"
table("yen.tex", "Simple paths (Yen--Lawler with A*) on two-way grids: time for the paths after the first (ms).",
      "tab:yen", " & " + " & ".join(TEX[l] for l in LANGS), body, "lrrr")

# CH
body = ""
for sc, label, plain in [("ch-200-two-way", r"$200^2$ two-way", None), ("ch-1000", r"$1000^2$ one-way",
                                                                         "eppstein-1000")]:
    body += label + " & " + " & ".join(best([val(sc, l, "contraction_ms") for l in LANGS])) + " & " + \
        " & ".join(best([val(sc, l, "repeated_ms") for l in LANGS])) + f" & {int(float(rows[(sc, 'Rust')]['shortcuts'])):,}".replace(",", "{,}") + " \\\\\n"
table("ch.tex", "Contraction hierarchy: preprocessing and the PHAST tree (repeated query), in ms, and the number of "
      "shortcuts (Rust; \\cs{} and Go differ slightly because of tie-breaking in witness searches).", "tab:ch",
      lang_header(["contraction", "tree (PHAST)"]) + " & shortcuts", body, "l" + "rrr" * 2 + "r")

# Runtime sensitivity (GC and JIT)
runtime = {}
with open(os.path.join(DATA, "runtime.csv")) as f:
    for r in csv.DictReader(f):
        runtime[(r["scenario"], r["language"], r["setting"])] = r
body = ""
for lang, setting, label in [("C#", "default", r"\cs{} default"), ("C#", "gen0 256 MB", r"\cs{} gen0 budget 256\,MB"),
                             ("C#", "no tiered JIT", r"\cs{} without tiered JIT"), ("Go", "default", "Go default"),
                             ("Go", "GOGC=off", r"Go \texttt{GOGC=off}"), ("Rust", "default", "Rust")]:
    cells = []
    for sc in ["eppstein-200", "eppstein-1000-k100k", "yen-200-k1000"]:
        r = runtime[(sc, lang, setting)]
        cells += [ms(float(r["repeated_ms"])), ms(float(r["next_ms"]))]
    body += label + " & " + " & ".join(cells) + " \\\\\n"
table("runtime.tex", "Runtime settings (median ms). Turning garbage collection off (Go) or making it rare (\\cs{}) has "
      "no measurable effect; disabling tiered compilation makes short \\cs{} runs 2--4$\\times$ faster.",
      "tab:runtime", " & \\multicolumn{2}{c}{Eppstein $200^2$, $k=10^4$} & \\multicolumn{2}{c}{Eppstein $1000^2$, "
      "$k=10^5$} & \\multicolumn{2}{c}{Yen $200^2$, $k=10^3$} \\\\ \\cmidrule(lr){2-3}\\cmidrule(lr){4-5}"
      "\\cmidrule(lr){6-7}\n & tree & next & tree & next & tree & next", body, "lrrrrrr")

# Number macros used in the text
macros = {}
ratios_tree, ratios_next = [], []
for sc in ["eppstein-1000", "eppstein-1000-k100k", "local-1000"]:  # the 10^6-vertex graphs
    t = [val(sc, l, "repeated_ms") for l in LANGS]
    n = [val(sc, l, "next_ms") for l in LANGS]
    ratios_tree.append(max(t) / min(t))
    ratios_next.append(max(n) / min(n))
macros["NumMaxRatioRepeated"] = f"{max(ratios_tree):.1f}"
macros["NumMaxRatioNext"] = f"{max(ratios_next):.1f}"
macros["NumLocalRepeated"] = f"{val('local-1000', 'Rust', 'repeated_ms'):.0f}\\,ms"
macros["NumLocalRatioRepeated"] = f"{val('local-1000-ratio', 'Rust', 'repeated_ms'):.1f}\\,ms"
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
    # size 3 is the first call in the process (JIT warm-up), not algorithm time
    coords = lambda version, key: " ".join(f"({r['size']},{max(float(r[key]), 0.001)})" for r in b
                                          if r["version"] == version and int(r["size"]) >= 4)
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


macros["NumLocCs"] = str(loc(files("Eppstein", r".*\.cs")))
macros["NumLocCsTests"] = str(loc(files("Eppstein.Tests", r".*\.cs")))
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

write("numbers.tex", "".join(f"\\newcommand{{\\{k}}}{{{v}}}\n" for k, v in sorted(macros.items())))
print("\n".join(f"{k} = {v}" for k, v in sorted(macros.items())))
