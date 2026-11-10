<#
.SYNOPSIS
  Builds the dashboard CSS with the Tailwind standalone CLI (no Node.js needed).
  Downloads the pinned CLI on first use and verifies its SHA-256 checksum.

.EXAMPLE
  ./scripts/tailwind.ps1            # one-off build (minified)
  ./scripts/tailwind.ps1 -Watch     # rebuild on every change while editing views
#>
param([switch]$Watch)

$ErrorActionPreference = 'Stop'
$Version = 'v4.3.3'
$Asset = 'tailwindcss-windows-x64.exe'

$root = Split-Path $PSScriptRoot -Parent
$dir = Join-Path $root "tools/tailwindcss/$Version"
$exe = Join-Path $dir 'tailwindcss.exe'

if (-not (Test-Path $exe)) {
    New-Item -ItemType Directory -Force $dir | Out-Null
    $base = "https://github.com/tailwindlabs/tailwindcss/releases/download/$Version"
    $tmp = Join-Path $dir "$Asset.download"
    Write-Host "Downloading Tailwind CLI $Version..."
    Invoke-WebRequest "$base/$Asset" -OutFile $tmp -UseBasicParsing
    $content = (Invoke-WebRequest "$base/sha256sums.txt" -UseBasicParsing).Content
    # GitHub serves the file as application/octet-stream, so PowerShell hands back bytes.
    $sums = if ($content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($content) } else { $content }
    $expected = ($sums -split "`n" | Where-Object { $_ -match [regex]::Escape($Asset) } | Select-Object -First 1) -split '\s+' | Select-Object -First 1
    $actual = (Get-FileHash $tmp -Algorithm SHA256).Hash
    if (-not $expected -or $actual -ne $expected.ToUpperInvariant()) {
        Remove-Item $tmp -Force
        throw "Checksum mismatch for $Asset (expected '$expected', got '$actual')."
    }
    Move-Item $tmp $exe
    Write-Host "Checksum OK."
}

$web = Join-Path $root 'src/TodayDbGate.Web'
$cliArgs = @('-i', (Join-Path $web 'Styles/app.css'), '-o', (Join-Path $web 'wwwroot/css/app.css'), '--minify')
if ($Watch) { $cliArgs += '--watch' }

# Tailwind prints progress to stderr; don't let Windows PowerShell treat that as a failure.
$ErrorActionPreference = 'Continue'
Push-Location $web
try {
    & $exe @cliArgs
    if ($LASTEXITCODE -ne 0) { throw "Tailwind CLI exited with code $LASTEXITCODE." }
} finally { Pop-Location }
