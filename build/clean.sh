#!/usr/bin/env bash
# Removes build output and packaged artifacts. Downloaded assets are left in place.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
find src tests -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
rm -rf artifacts
echo "Clean. Run build/fetch-assets.sh if assets/ is empty."
