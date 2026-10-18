# Builds MagicMouseEnhancer.exe with the .NET Framework compiler that ships with
# Windows (C# 5, target .NET Framework 4.8 — present on every Windows 10/11).
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $fw "csc.exe"

if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }

$icon = Join-Path $root "assets\app.ico"
if (-not (Test-Path $icon)) {
    Write-Output "Generating app icon..."
    & (Join-Path $root "gen-icon.ps1")
    if (-not (Test-Path $icon)) { throw "icon generation failed" }
}

$outDir = Join-Path $root "bin"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$refs = @(
    (Join-Path $fw "System.dll"),
    (Join-Path $fw "System.Core.dll"),
    (Join-Path $fw "System.Drawing.dll"),
    (Join-Path $fw "System.Windows.Forms.dll")
)
$refArgs = @()
foreach ($r in $refs) { $refArgs += "/r:`"$r`"" }

$srcFiles = @()
foreach ($f in (Get-ChildItem (Join-Path $root "src") -Filter *.cs | Sort-Object Name)) {
    $srcFiles += "`"$($f.FullName)`""
}

$outExe = Join-Path $outDir "MagicMouseEnhancer.exe"
$args = @(
    "/nologo", "/target:winexe", "/platform:anycpu", "/optimize+",
    "/win32icon:`"$icon`"",
    "/out:`"$outExe`""
) + $refArgs + $srcFiles

Write-Output "Compiling with $csc ..."
& $csc @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item $icon (Join-Path $outDir "app.ico") -Force
Write-Output "Build OK -> $outExe"
