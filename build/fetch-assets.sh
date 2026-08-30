#!/usr/bin/env bash
# Downloads the runtime assets the application bundles: the interface/PDF font and the
# Tesseract language data. Both are redistributable; see THIRD_PARTY_NOTICES.md.
#
# These files are binary and are deliberately not committed. Run this once after cloning,
# before building a package. Nothing is downloaded when the application runs.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fonts="$root/assets/fonts"
tessdata="$root/assets/tessdata"
mkdir -p "$fonts" "$tessdata"

FONT_BASE="https://raw.githubusercontent.com/hafontia-zz/Assistant/master/Fonts/TTF"
TESS_BASE="https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main"
OFL_URL="https://raw.githubusercontent.com/hafontia-zz/Assistant/master/OFL.txt"

fetch() {
  local url="$1" target="$2"
  if [ -s "$target" ]; then
    echo "  present  $(basename "$target")"
    return
  fi
  echo "  fetching $(basename "$target")"
  curl -fsSL --retry 3 --retry-delay 2 -o "$target.part" "$url"
  mv "$target.part" "$target"
}

echo "Fonts (SIL Open Font License 1.1):"
fetch "$FONT_BASE/Assistant-Regular.ttf" "$fonts/Assistant-Regular.ttf"
fetch "$FONT_BASE/Assistant-Bold.ttf"    "$fonts/Assistant-Bold.ttf"
fetch "$OFL_URL"                         "$fonts/OFL.txt"

echo "OCR language data (Apache License 2.0):"
fetch "$TESS_BASE/heb.traineddata" "$tessdata/heb.traineddata"
fetch "$TESS_BASE/eng.traineddata" "$tessdata/eng.traineddata"

echo
echo "Done. Assets are in assets/fonts and assets/tessdata and are copied into the build output."
