# Windows equivalent of build/fetch-assets.sh.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fonts = Join-Path $root 'assets/fonts'
$tessdata = Join-Path $root 'assets/tessdata'
New-Item -ItemType Directory -Force $fonts, $tessdata | Out-Null

$fontBase = 'https://raw.githubusercontent.com/hafontia-zz/Assistant/master/Fonts/TTF'
$tessBase = 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main'

function Fetch($url, $target) {
    if (Test-Path $target) { Write-Host "  present  $(Split-Path -Leaf $target)"; return }
    Write-Host "  fetching $(Split-Path -Leaf $target)"
    Invoke-WebRequest -Uri $url -OutFile $target -UseBasicParsing
}

Write-Host 'Fonts (SIL Open Font License 1.1):'
Fetch "$fontBase/Assistant-Regular.ttf" (Join-Path $fonts 'Assistant-Regular.ttf')
Fetch "$fontBase/Assistant-Bold.ttf"    (Join-Path $fonts 'Assistant-Bold.ttf')
Fetch 'https://raw.githubusercontent.com/hafontia-zz/Assistant/master/OFL.txt' (Join-Path $fonts 'OFL.txt')

Write-Host 'OCR language data (Apache License 2.0):'
Fetch "$tessBase/heb.traineddata" (Join-Path $tessdata 'heb.traineddata')
Fetch "$tessBase/eng.traineddata" (Join-Path $tessdata 'eng.traineddata')
