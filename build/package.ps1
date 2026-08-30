# Windows equivalent of build/package.sh. Produces the portable folder, a zip and a checksum.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$version = ([regex]'<Version>([^<]+)</Version>').Match((Get-Content Directory.Build.props -Raw)).Groups[1].Value
$rid = if ($args.Count -gt 0) { $args[0] } else { 'win-x64' }
$name = "PdfEditor-$version-$rid-portable"
$out = "artifacts/$name"

if (-not (Test-Path 'assets/fonts/Assistant-Regular.ttf') -or -not (Test-Path 'assets/tessdata/heb.traineddata')) {
    throw 'Assets are missing. Run build/fetch-assets.ps1 first.'
}

Remove-Item -Recurse -Force $out, "artifacts/$name.zip" -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force artifacts | Out-Null

dotnet publish src/PdfEditor.App -c Release -r $rid --self-contained true `
    -p:DebugType=none -p:DebugSymbols=false -o $out

foreach ($required in @('PdfEditor.exe', 'pdfium.dll', 'libSkiaSharp.dll',
                        'fonts/Assistant-Regular.ttf', 'tessdata/heb.traineddata',
                        'tessdata/eng.traineddata', 'x64/tesseract50.dll')) {
    if (-not (Test-Path (Join-Path $out $required))) { throw "Package is missing $required" }
}

Copy-Item README.md, THIRD_PARTY_NOTICES.md, LICENSE $out -ErrorAction SilentlyContinue
Compress-Archive -Path $out -DestinationPath "artifacts/$name.zip" -Force
(Get-FileHash "artifacts/$name.zip" -Algorithm SHA256).Hash + "  $name.zip" |
    Set-Content artifacts/SHA256SUMS.txt

Write-Host "Built $out"
Get-Content artifacts/SHA256SUMS.txt
