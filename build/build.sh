#!/usr/bin/env bash
# Restores and builds the whole solution in Release.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd "$root"
dotnet restore PdfEditor.sln
dotnet build PdfEditor.sln -c "${1:-Release}" --no-restore
