#!/usr/bin/env bash
# Checks that the version given as the argument (for example 2.0.0) is the version of the C#
# package, the Rust crate and CITATION.cff, and that the Go module path has the matching major
# version. Used by the release workflow; run it by hand before tagging a release.
# Usage: .github/scripts/check-version.sh 2.0.0
set -euo pipefail
version=${1:?usage: check-version.sh X.Y.Z}
root=$(cd "$(dirname "$0")/../.." && pwd)
major=${version%%.*}
status=0
check() { # name actual expected
  if [ "$2" = "$3" ]; then
    echo "ok       $1: $2"
  else
    echo "MISMATCH $1: $2 (expected $3)"
    status=1
  fi
}
check "csharp/Eppstein/Eppstein.csproj" "$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/csharp/Eppstein/Eppstein.csproj")" "$version"
check "rust/Cargo.toml" "$(sed -n 's/^version = "\(.*\)"/\1/p' "$root/rust/Cargo.toml" | head -1)" "$version"
check "CITATION.cff" "$(sed -n 's/^version: //p' "$root/CITATION.cff")" "$version"
expected=github.com/nickdinges/k-shortest-path/go
[ "$major" -ge 2 ] && expected=$expected/v$major
check "go/go.mod (module path)" "$(sed -n 's/^module //p' "$root/go/go.mod")" "$expected"
exit $status
