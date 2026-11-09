#!/usr/bin/env python3
"""Runtime sensitivity experiments: how much of the time is garbage collection and JIT warm-up?

- Go with GOGC=off (no garbage collection at all) vs. the default.
- C# with a 256 MB generation-0 budget (DOTNET_GCgen0size, far fewer collections) vs. the default.
- C# with tiered compilation disabled (DOTNET_TieredCompilation=0: fully optimized code from the
  first call) vs. the default (quick tier-0 code first, optimized later).
Writes paper/data/runtime.csv (medians of REPEAT fresh processes). Run run_benchmarks.py first
(it builds the programs). Usage: python3 paper/scripts/run_runtime_experiments.py [--repeat N]
"""
import csv
import os
import statistics
import subprocess
import sys

sys.path.insert(0, os.path.dirname(__file__))
import run_benchmarks as rb  # noqa: E402

REPEAT = int(sys.argv[sys.argv.index("--repeat") + 1]) if "--repeat" in sys.argv else 5
SCENARIOS = [("eppstein-200", "200 --k 10000 --build index"),
             ("eppstein-1000-k100k", "1000 --k 100000 --build index"),
             ("yen-200-k1000", "200 --k 1000 --two-way --simple --build index")]
SETTINGS = [("C#", "default", {}), ("C#", "gen0 256 MB", {"DOTNET_GCgen0size": "0x10000000"}),
            ("C#", "no tiered JIT", {"DOTNET_TieredCompilation": "0"}),
            ("Go", "default", {}), ("Go", "GOGC=off", {"GOGC": "off"}), ("Rust", "default", {})]


def main():
    out = os.path.join(rb.ROOT, "paper", "data", "runtime.csv")
    with open(out, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["scenario", "language", "setting", "first_ms", "repeated_ms", "next_ms", "memory_mb"])
        for name, args in SCENARIOS:
            for lang, setting, env in SETTINGS:
                old = {k: os.environ.get(k) for k in env}
                os.environ.update(env)
                try:
                    runs = [rb.run_once(lang, args) for _ in range(REPEAT)]
                finally:
                    for k, v in old.items():
                        if v is None:
                            os.environ.pop(k, None)
                        else:
                            os.environ[k] = v
                med = [statistics.median(r[k] for r in runs) for k in ["first_ms", "repeated_ms", "next_ms",
                                                                       "memory_mb"]]
                w.writerow([name, lang, setting] + med)
                f.flush()
                print(name, lang, setting, med, flush=True)
    print("written", out)


if __name__ == "__main__":
    main()
