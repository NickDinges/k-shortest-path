#!/usr/bin/env python3
"""Runs the identical grid benchmark of the C#, Go and Rust implementations and writes the
medians to paper/data/results.csv.

Every scenario runs REPEAT times per language, each in a fresh process. All three programs use the
same graph generator (SplitMix64, seed 1), so the reported weight of the last path must agree;
the script stops if it does not.

Usage (from the repository root): python3 paper/scripts/run_benchmarks.py [--repeat N] [--quick]
"""
import csv
import os
import re
import statistics
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
REPEAT = int(sys.argv[sys.argv.index("--repeat") + 1]) if "--repeat" in sys.argv else 5
QUICK = "--quick" in sys.argv

# name, benchmark arguments, repetitions factor (1 = REPEAT, 0 = once)
SCENARIOS = [
    ("eppstein-200", "200 --k 10000 --build index", 1),
    ("eppstein-500", "500 --k 10000 --build index", 1),
    ("eppstein-1000", "1000 --k 10000 --build index", 1),
    ("eppstein-1000-k100k", "1000 --k 100000 --build index", 1),
    ("local-1000", "1000 --k 10000 --local --build index", 1),
    ("local-1000-ratio", "1000 --k 10000 --local --ratio 0.1 --build index", 1),
    ("build-strings-1000", "1000 --k 1 --build strings", 1),
    ("build-dimacs-1000", "1000 --k 1 --build dimacs", 1),
    ("yen-200-k100", "200 --k 100 --two-way --simple --build index", 1),
    ("yen-200-k1000", "200 --k 1000 --two-way --simple --build index", 1),
    ("yen-local-1000-k100", "1000 --k 100 --local --two-way --simple --ratio 0.1 --build index", 1),
    ("ch-200-two-way", "200 --k 1 --two-way --ch --build index", 1),
    ("ch-1000", "1000 --k 1 --ch --build index", 0),
]
if QUICK:
    SCENARIOS = [s for s in SCENARIOS if "1000" not in s[0] or s[0] == "local-1000-ratio"]


def build():
    subprocess.run(["dotnet", "build", "-c", "Release", "Eppstein.Benchmarks", "-v", "q"], cwd=ROOT, check=True,
                   stdout=subprocess.DEVNULL)
    subprocess.run(["go", "build", "-o", os.path.join(ROOT, "go", "benchmark.bin"), "./cmd/benchmark"],
                   cwd=os.path.join(ROOT, "go"), check=True)
    subprocess.run(["cargo", "build", "--release", "--quiet", "--bin", "benchmark"], cwd=os.path.join(ROOT, "rust"),
                   check=True)


COMMANDS = {
    "C#": ["dotnet", os.path.join(ROOT, "Eppstein.Benchmarks/bin/Release/net10.0/Eppstein.Benchmarks.dll")],
    "Go": [os.path.join(ROOT, "go", "benchmark.bin")],
    "Rust": [os.path.join(ROOT, "rust/target/release/benchmark")],
}
ROW = re.compile(r"^\| (\d+)x\d+ \| (\d+) \| (\d+) \| ([\d.]+) ms \| ([\d.]+) MB \| ([\d.]+) ms \| ([\d.]+) ms \| "
                 r"([\d.]+) ms \| (\d+) \| (-?\d+) \|$")
CH = re.compile(r"contraction (\d+) ms, (\d+) shortcuts")
FIELDS = ["build_ms", "memory_mb", "first_ms", "repeated_ms", "next_ms"]


def run_once(language, args):
    result = subprocess.run(COMMANDS[language] + args.split(), capture_output=True, text=True, check=True)
    rows = [ROW.match(line) for line in result.stdout.splitlines()]
    row = next(m for m in rows if m)
    values = dict(zip(FIELDS, map(float, row.groups()[3:8])))
    values.update(vertices=int(row.group(2)), edges=int(row.group(3)), found=int(row.group(9)),
                  last=int(row.group(10)))
    ch = CH.search(result.stderr)
    values["contraction_ms"] = float(ch.group(1)) if ch else ""
    values["shortcuts"] = int(ch.group(2)) if ch else ""
    return values


def main():
    build()
    out = os.path.join(ROOT, "paper", "data", "results.csv")
    with open(out, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["scenario", "language", "runs", "vertices", "edges", "found", "last"] + FIELDS +
                        ["contraction_ms", "shortcuts"])
        for name, args, factor in SCENARIOS:
            last_weights = {}
            for language in COMMANDS:
                runs = [run_once(language, args) for _ in range(REPEAT if factor else 1)]
                med = {k: statistics.median(r[k] for r in runs) for k in FIELDS}
                ch = [r["contraction_ms"] for r in runs if r["contraction_ms"] != ""]
                first = runs[0]
                last_weights[language] = (first["found"], first["last"])
                writer.writerow([name, language, len(runs), first["vertices"], first["edges"], first["found"],
                                 first["last"]] + [med[k] for k in FIELDS] +
                                [statistics.median(ch) if ch else "", first["shortcuts"]])
                f.flush()
                print(f"{name:24} {language:5} " + " ".join(f"{k}={med[k]:.1f}" for k in FIELDS) +
                      (f" contraction={statistics.median(ch):.0f}" if ch else ""), flush=True)
            if len(set(last_weights.values())) != 1:
                sys.exit(f"results differ between languages in {name}: {last_weights}")
    print("written", out)


if __name__ == "__main__":
    main()
