#!/usr/bin/env bash
# Runs the C# and Go benchmarks of two checkouts (base and head) one after the other on the same
# machine and prints a Markdown comparison. Used by the CI workflow for pull requests.
# Usage: compare-benchmarks.sh BASE_DIR HEAD_DIR
# Shared CI machines are noisy: treat differences below ~10-20% as noise.
set -uo pipefail
base=$(cd "$1" && pwd)
head=$(cd "$2" && pwd)
work=$(mktemp -d)

# C# benchmark project (in csharp/ since the folder move; it was called Eppstein2.Benchmarks before version 2)
csharp_project() {
  for p in csharp/Eppstein.Benchmarks Eppstein.Benchmarks Eppstein2.Benchmarks; do
    [ -d "$1/$p" ] && { echo "$1/$p"; return; }
  done
}

run_csharp() { # dir label
  local project
  project=$(csharp_project "$1")
  if [ -z "$project" ]; then
    echo "_No C# benchmark in $2._"
    return
  fi
  if ! dotnet build -c Release "$project" -v q >"$work/build-$2.log" 2>&1; then
    echo "_C# benchmark of $2 does not build._"
    return
  fi
  local dll
  dll=$(find "$project/bin/Release" -name "$(basename "$project").dll" | head -1)
  for args in "200 1000 --k 10000 --build index" "1000 --local --ratio 0.1 --build index"; do
    dotnet "$dll" $args 2>/dev/null | grep -E '^\||^[A-Z]' || echo "_Failed: $args_"
    echo
  done
}

echo "## Benchmark: base vs. this pull request"
echo
echo "Measured one after the other on the same CI machine; differences below ~10-20% are noise."
echo
echo "### C# (\`Eppstein.Benchmarks\`)"
for side in base head; do
  dir=$base
  [ "$side" = head ] && dir=$head
  echo
  echo "#### $side"
  echo
  run_csharp "$dir" "$side"
done

echo
echo "### Go (\`go test -bench\`, compared with benchstat)"
echo
for side in base head; do
  dir=$base
  [ "$side" = head ] && dir=$head
  (cd "$dir/go" 2>/dev/null && go test -run none -bench . -benchmem -count 6 ./eppstein >"$work/$side.txt" 2>&1) ||
    echo "_Go benchmarks of $side failed or do not exist._"
done
if command -v benchstat >/dev/null 2>&1; then
  echo '```'
  (cd "$work" && benchstat base=base.txt head=head.txt)
  echo '```'
else
  echo '```'
  grep -h Benchmark "$work/base.txt" "$work/head.txt"
  echo '```'
fi
rm -rf "$work"
