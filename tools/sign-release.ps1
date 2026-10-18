<#
.SYNOPSIS
    Authenticode-sign built Windows executables with a SHA-256 signature and an
    RFC 3161 timestamp, then verify the result.

.DESCRIPTION
    Nothing here can sign a file without a code-signing certificate that Windows
    trusts. This script exists so that once you have one, signing is a single
    command instead of a research project.

    Two ways to point it at a certificate:

      -Thumbprint <hex>   a certificate already in your store (hardware token,
                          SimplySign / cloud HSM, or an imported PFX)
      -Pfx <file>         a .pfx/.p12 file; password from -PfxPassword or the
                          SIGN_PFX_PASSWORD environment variable

    Timestamping is not optional: without it the signature stops being valid the
    day the certificate expires. Always sign with a timestamp.

.EXAMPLE
    # Sign every built installer and exe in release\ using a store certificate
    .\tools\sign-release.ps1 -Path .\release\*.exe -Thumbprint 1a2b3c4d5e6f...

.EXAMPLE
    # Sign from a PFX, password supplied via the environment
    $env:SIGN_PFX_PASSWORD = "..."   # do not hard-code this in a file
    .\tools\sign-release.ps1 -Path .\bin\*.exe -Pfx .\codesign.pfx

.EXAMPLE
    # Show exactly what would be signed, without touching anything
    .\tools\sign-release.ps1 -Path .\release\*.exe -Thumbprint 1a2b... -DryRun
#>
[CmdletBinding()]
param(
    [string[]] $Path,
    [string]   $Thumbprint,
    [string]   $Subject,
    [string]   $Pfx,
    [string]   $PfxPassword,
    [string]   $Description,
    [string]   $TimestampUrl = "http://timestamp.digicert.com",
    [switch]   $DryRun
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
    $roots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    )
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $hit = Get-ChildItem $root -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
               Where-Object { $_.FullName -match '\\x64\\' } |
               Sort-Object FullName -Descending |
               Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

function Write-Head($msg) { Write-Host ""; Write-Host "== $msg ==" -ForegroundColor Cyan }

# ---------------------------------------------------------------- signtool
$signtool = Find-SignTool
if (-not $signtool) {
    Write-Host "signtool.exe not found." -ForegroundColor Red
    Write-Host "Install the Windows SDK signing tools:"
    Write-Host "  winget install Microsoft.WindowsSDK.10.0.26100"
    exit 1
}
Write-Host "signtool: $signtool" -ForegroundColor DarkGray

# ---------------------------------------------------------------- which files
if (-not $Path) { $Path = @(".\release\*.exe") }
$files = @()
foreach ($p in $Path) {
    $resolved = @(Get-Item -Path $p -ErrorAction SilentlyContinue | Where-Object { -not $_.PSIsContainer })
    if (-not $resolved) {
        Write-Host "No file matched: $p" -ForegroundColor Yellow
        continue
    }
    foreach ($f in $resolved) {
        # Never sign a checksum or a blockmap - only real PE binaries.
        if ($f.Extension -in @(".exe", ".dll", ".msi", ".sys", ".ps1", ".cat")) { $files += $f }
    }
}
$files = $files | Sort-Object FullName -Unique

if (-not $files) {
    Write-Host "Nothing signable matched the -Path you gave." -ForegroundColor Red
    exit 1
}

Write-Head "Files to sign ($($files.Count))"
$files | ForEach-Object { Write-Host ("  {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }

# ---------------------------------------------------------------- certificate
Write-Head "Certificate"

$certArgs = @()
$tempPfx = $null

if ($Thumbprint -or $Subject) {
    $store = @("Cert:\CurrentUser\My", "Cert:\LocalMachine\My")
    $cert  = $null
    foreach ($s in $store) {
        if ($Thumbprint) {
            $clean = ($Thumbprint -replace '[^0-9A-Fa-f]', '').ToUpper()
            $cert = Get-ChildItem $s -ErrorAction SilentlyContinue |
                    Where-Object { $_.Thumbprint -eq $clean } | Select-Object -First 1
        } elseif ($Subject) {
            $cert = Get-ChildItem $s -CodeSigningCert -ErrorAction SilentlyContinue |
                    Where-Object { $_.Subject -like "*$Subject*" } | Select-Object -First 1
        }
        if ($cert) { break }
    }

    if (-not $cert) {
        Write-Host "No matching certificate in CurrentUser\My or LocalMachine\My." -ForegroundColor Red
        Write-Host "List what you have with:  Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Format-List Subject,Thumbprint,NotAfter"
        exit 2
    }
    if (-not $cert.HasPrivateKey) {
        Write-Host "Certificate '$($cert.Subject)' has no private key - it cannot sign." -ForegroundColor Red
        Write-Host "If it is on a hardware token, plug the token in and unlock it first."
        exit 2
    }

    $daysLeft = [int]($cert.NotAfter - (Get-Date)).TotalDays
    Write-Host ("  subject : {0}" -f $cert.Subject)
    Write-Host ("  issuer  : {0}" -f $cert.Issuer)
    Write-Host ("  expires : {0:yyyy-MM-dd}  ({1} days)" -f $cert.NotAfter, $daysLeft)
    if ($daysLeft -le 0) { Write-Host "  EXPIRED - this signature will not be trusted." -ForegroundColor Red }

    $certArgs = @("/sha1", $cert.Thumbprint)

} elseif ($Pfx) {
    if (-not (Test-Path $Pfx)) {
        Write-Host "PFX not found: $Pfx" -ForegroundColor Red
        exit 2
    }
    if (-not $PfxPassword) { $PfxPassword = $env:SIGN_PFX_PASSWORD }
    if (-not $PfxPassword) {
        Write-Host "PFX supplied but no password. Pass -PfxPassword or set `$env:SIGN_PFX_PASSWORD." -ForegroundColor Red
        exit 2
    }
    Write-Host "  pfx     : $Pfx"
    $certArgs = @("/f", (Resolve-Path $Pfx).Path, "/p", $PfxPassword)

} else {
    Write-Host "No certificate supplied - nothing was signed." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "This is expected if you have not bought a code-signing certificate yet."
    Write-Host "Windows will show SmartScreen's 'unrecognised app' prompt until a"
    Write-Host "certificate from a trusted CA is used. See SIGNING.md."
    Write-Host ""
    Write-Host "Re-run with -Thumbprint <hex> or -Pfx <file>."
    exit 2
}

# ---------------------------------------------------------------- sign
Write-Head "Signing"

$signArgs = @(
    "sign",
    "/fd", "sha256",
    "/tr", $TimestampUrl,
    "/td", "sha256"
)
if ($Description) { $signArgs += @("/d", $Description) }
$signArgs += $certArgs

foreach ($f in $files) {
    Write-Host ("  -> {0}" -f $f.Name)
    if ($DryRun) {
        Write-Host ("     [dry run] signtool {0} `"{1}`"" -f ($signArgs -join ' '), $f.FullName) -ForegroundColor DarkGray
        continue
    }
    & $signtool @signArgs $f.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Host "     sign FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

if ($DryRun) {
    Write-Head "Dry run complete - nothing changed"
    exit 0
}

# ---------------------------------------------------------------- verify
Write-Head "Verifying signature chain"

$failed = 0
foreach ($f in $files) {
    Write-Host ("  {0}" -f $f.Name)
    & $signtool verify /pa /all $f.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Host "     VERIFY FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
        $failed++
    } else {
        $sig = Get-AuthenticodeSignature $f.FullName
        Write-Host ("     OK  status={0}  signer={1}" -f $sig.Status, $sig.SignerCertificate.Subject) -ForegroundColor Green
    }
}

Write-Host ""
if ($failed -gt 0) {
    Write-Host "$failed file(s) failed verification." -ForegroundColor Red
    Write-Host "A signature that Windows does not chain to a trusted root is worse than none:"
    Write-Host "it shows an 'unknown publisher' name instead of an anonymous one."
    exit 1
}
Write-Host "All $($files.Count) file(s) signed and verified." -ForegroundColor Green
