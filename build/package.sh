#!/usr/bin/env bash
# Produces the release artifacts:
#   artifacts/PdfEditor-<version>-win-x64-portable/   a self-contained folder, the supported build
#   artifacts/PdfEditor-<version>-win-x64-portable.zip
#   artifacts/SHA256SUMS.txt
#
# Cross-publishing from Linux is supported and is how the artifact is produced in CI.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd "$root"

version="$(grep -oPm1 '(?<=<Version>)[^<]+' Directory.Build.props)"
rid="${1:-win-x64}"
name="PdfEditor-$version-$rid-portable"
out="artifacts/$name"

if [ ! -s assets/fonts/Assistant-Regular.ttf ] || [ ! -s assets/tessdata/heb.traineddata ]; then
  echo "Assets are missing. Run build/fetch-assets.sh first." >&2
  exit 1
fi

rm -rf "$out" "artifacts/$name.zip"
mkdir -p artifacts

dotnet publish src/PdfEditor.App -c Release -r "$rid" --self-contained true \
  -p:DebugType=none -p:DebugSymbols=false -o "$out"

# The package must be able to run with no network and no developer tools.
for required in PdfEditor.exe pdfium.dll libSkiaSharp.dll fonts/Assistant-Regular.ttf \
                tessdata/heb.traineddata tessdata/eng.traineddata x64/tesseract50.dll; do
  [ -e "$out/$required" ] || { echo "Package is missing $required" >&2; exit 1; }
done

cp README.md THIRD_PARTY_NOTICES.md LICENSE "$out/" 2>/dev/null || true

( cd artifacts && zip -qr "$name.zip" "$name" )
( cd artifacts && sha256sum "$name.zip" > SHA256SUMS.txt )

echo
echo "Built $out"
du -sh "$out"
cat artifacts/SHA256SUMS.txt
