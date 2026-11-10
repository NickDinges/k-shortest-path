#!/usr/bin/env python3
"""Runs the identical grid benchmark of the C#, Go and Rust implementations and writes the
medians to paper/data/results.csv.

Every scenario runs REPEAT times per language, each in a fresh process. All three programs use the
same graph generator (SplitMix64, seed 1), so the reported weight of the last path must agree;
the script stops if it does not.

Per process the benchmark reports the first query (cold), the second identical query, and the
median of REPS further identical queries (steady state: optimized code, warm caches). The CSV has
the median over the processes of each value, the smallest and largest steady-state and next-paths
time (dispersion), the garbage collections, their pause time and the bytes allocated while the next
paths were enumerated, and the peak resident memory of the whole process.

Usage (from the repository root): python3 paper/scripts/run_benchmarks.py [--repeat N] [--quick]
"""
import csv
import os
import re
import statistics
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
REPEAT = int(sys.argv[sys.argv.index("--repeat") + 1]) if "--repeat" in sys.argv else 5
QUICK = "--quick" in sys.argv

REPS = 20  # identical queries per process for the steady-state time

# name, benchmark arguments, repetitions factor (1 = REPEAT, 0 = once)
SCENARIOS = [
    ("eppstein-200", "200 --k 10000 --build index", 1),
    ("eppstein-500", "500 --k 10000 --build index", 1),
    ("eppstein-1000", "1000 --k 10000 --build index", 1),
    ("eppstein-1000-k100k", "1000 --k 100000 --build index", 1),
    ("weights-1000-k100k", "1000 --k 100000 --build index --weights", 1),
    ("local-1000", "1000 --k 10000 --local --build index", 1),
    ("local-1000-ratio", "1000 --k 10000 --local --ratio 0.1 --build index", 1),
    ("build-strings-1000", "1000 --k 1 --build strings", 1),
    ("build-dimacs-1000", "1000 --k 1 --build dimacs", 1),
    ("yen-200-k100", "200 --k 100 --two-way --simple --build index", 1),
    ("yen-200-k1000", "200 --k 1000 --two-way --simple --build index", 1),
    ("yen-local-1000-k100", "1000 --k 100 --local --two-way --simple --ratio 0.1 --build index", 1),
    ("plain-200-two-way", "200 --k 1 --two-way --build index", 1),
    ("arcflags-4-200-two-way", "200 --k 1 --two-way --arc-flags 4 --build index", 1),
    ("arcflags-16-200-two-way", "200 --k 1 --two-way --arc-flags 16 --build index", 0),
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
STATS = re.compile(r"^stats: (.*)$", re.M)
FIELDS = ["build_ms", "memory_mb", "first_ms", "repeated_ms", "next_ms"]
STAT_FIELDS = ["warm_median_ms", "gc_count", "gc_pause_ms", "alloc_mb", "arcflags_ms", "peak_rss_mb"]


def run_once(language, args):
    """One fresh process. wait4 gives the resource usage of exactly this child (peak resident memory)."""
    with tempfile.TemporaryFile("w+") as out, tempfile.TemporaryFile("w+") as err:
        process = subprocess.Popen(COMMANDS[language] + args.split() + ["--reps", str(REPS)], stdout=out, stderr=err)
        _, status, usage = os.wait4(process.pid, 0)
        process.returncode = os.waitstatus_to_exitcode(status)
        out.seek(0)
        err.seek(0)
        stdout, stderr = out.read(), err.read()
    if process.returncode != 0:
        sys.exit(f"{language} {args}: exit code {process.returncode}\n{stderr}")
    rows = [ROW.match(line) for line in stdout.splitlines()]
    row = next(m for m in rows if m)
    values = dict(zip(FIELDS, map(float, row.groups()[3:8])))
    values.update(vertices=int(row.group(2)), edges=int(row.group(3)), found=int(row.group(9)),
                  last=int(row.group(10)))
    ch = CH.search(stderr)
    values["contraction_ms"] = float(ch.group(1)) if ch else ""
    values["shortcuts"] = int(ch.group(2)) if ch else ""
    values.update((k, float(v)) for k, v in (item.split("=") for item in STATS.search(stderr).group(1).split()))
    values["peak_rss_mb"] = usage.ru_maxrss / 1024  # Linux reports kilobytes
    return values


def main():
    build()
    out = os.path.join(ROOT, "paper", "data", "results.csv")
    with open(out, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["scenario", "language", "runs", "vertices", "edges", "found", "last"] + FIELDS +
                        ["contraction_ms", "shortcuts"] + STAT_FIELDS + ["warm_lo_ms", "warm_hi_ms", "next_lo_ms",
                                                                          "next_hi_ms"])
        for name, args, factor in SCENARIOS:
            last_weights = {}
            for language in COMMANDS:
                runs = [run_once(language, args) for _ in range(REPEAT if factor else 1)]
                med = {k: statistics.median(r[k] for r in runs) for k in FIELDS + STAT_FIELDS}
                spread = [f(r[k] for r in runs) for k in ("warm_median_ms", "next_ms") for f in (min, max)]
                ch = [r["contraction_ms"] for r in runs if r["contraction_ms"] != ""]
                first = runs[0]
                last_weights[language] = (first["found"], first["last"])
                writer.writerow([name, language, len(runs), first["vertices"], first["edges"], first["found"],
                                 first["last"]] + [med[k] for k in FIELDS] +
                                [statistics.median(ch) if ch else "", first["shortcuts"]] +
                                [round(med[k], 3) for k in STAT_FIELDS] + spread)
                f.flush()
                print(f"{name:24} {language:5} " + " ".join(f"{k}={med[k]:.1f}" for k in FIELDS) +
                      f" warm={med['warm_median_ms']:.2f} gc={med['gc_count']:.0f}/{med['gc_pause_ms']:.1f}ms"
                      f" alloc={med['alloc_mb']:.0f}MB rss={med['peak_rss_mb']:.0f}MB" +
                      (f" contraction={statistics.median(ch):.0f}" if ch else ""), flush=True)
            if len(set(last_weights.values())) != 1:
                sys.exit(f"results differ between languages in {name}: {last_weights}")
    print("written", out)


if __name__ == "__main__":
    main()
