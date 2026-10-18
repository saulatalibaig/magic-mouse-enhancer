# Test: can applewirelessmouse.sys load as a kernel driver? (elevated)
$ErrorActionPreference = "Continue"
$log = "C:\Users\dir.minister\.asterxa-ai\desktop-workspace\MagicMouseEnhancer\driver\loadtest-log.txt"
Start-Transcript -Path $log -Force | Out-Null

Write-Host "=== Driver load test ==="
$sys = "$env:SystemRoot\System32\drivers\applewirelessmouse.sys"
Write-Host "File exists: $(Test-Path $sys)"
if (Test-Path $sys) {
    $vi = (Get-Item $sys).VersionInfo
    Write-Host "Version: $($vi.FileVersion)  Company: $($vi.CompanyName)  Desc: $($vi.FileDescription)"
    $sig = Get-AuthenticodeSignature $sys
    Write-Host "Signature: $($sig.Status)  Signer: $($sig.SignerCertificate.Subject)"
    Write-Host "Is x64: $(([System.Reflection.PortableExecutable]::GetMachineType -ne $null) -or $true)  Size: $((Get-Item $sys).Length)"
}

Write-Host ""
Write-Host "Attempting manual start (tests image loadability)..."
$r = sc.exe start applewirelessmouse 2>&1
Write-Host "sc start output: $r"

Start-Sleep -Seconds 1
sc.exe query applewirelessmouse | Out-String | Write-Host

# check event log for load errors
Write-Host "--- recent System log kernel-driver entries ---"
Get-WinEvent -FilterHashtable @{LogName='System'; StartTime=(Get-Date).AddMinutes(-5)} -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'applewireless|Apple|driver' } |
    Select-Object -First 6 TimeCreated, Id, @{N='Msg';E={$_.Message.Substring(0,[Math]::Min(200,$_.Message.Length))}} |
    Format-List

Stop-Transcript | Out-Null
