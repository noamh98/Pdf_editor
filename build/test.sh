#!/usr/bin/env bash
# Runs every test project. Recognition itself needs Windows; everything else runs anywhere.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd "$root"
dotnet test PdfEditor.sln -c "${1:-Release}" --logger "console;verbosity=minimal"
