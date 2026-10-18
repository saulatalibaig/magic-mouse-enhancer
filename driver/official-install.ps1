# Install Apple's OFFICIAL AppleWirelessMouse driver package (elevated)
$ErrorActionPreference = "Continue"
$log = "C:\Users\dir.minister\.asterxa-ai\desktop-workspace\MagicMouseEnhancer\driver\official-install-log.txt"
Start-Transcript -Path $log -Force | Out-Null

Write-Host "=== Install official Apple driver package (Boot Camp 6.1.14) ==="

# clean any leftover manual service from earlier attempts
if (Get-Service applewirelessmouse -ErrorAction SilentlyContinue) {
    sc.exe stop applewirelessmouse 2>&1 | Out-Null
    sc.exe delete applewirelessmouse 2>&1 | Out-Null
    Write-Host "Removed leftover manual service" -ForegroundColor Yellow
}
Start-Sleep -Milliseconds 500

$inf = "C:\Users\dir.minister\.asterxa-ai\desktop-workspace\MagicMouseEnhancer\driver\apple-official\extracted\AppleWirelessMouse\AppleWirelessMouse.inf"
Write-Host "Adding driver package to DriverStore..." -ForegroundColor Yellow
pnputil.exe /add-driver $inf /install 2>&1 | Out-String | Write-Host

# restart the mouse device so PnP rebinds with the new filter
$parent = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    $_.InstanceId -match "^BTHENUM\\\{00001124-0000-1000-8000-00805f9b34fb\}" -and
    $_.InstanceId -match "004C|05AC" -and $_.Class -eq "HIDClass"
} | Select-Object -First 1
if ($parent) {
    Write-Host "Restarting device: $($parent.InstanceId)" -ForegroundColor Yellow
    pnputil.exe /restart-device "$($parent.InstanceId)" 2>&1 | Out-String | Write-Host
}
Start-Sleep -Seconds 4

Write-Host "=== State ===" -ForegroundColor Cyan
sc.exe query applewirelessmouse 2>&1 | Out-String | Write-Host
Write-Host "Service config:"
Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\applewirelessmouse" -ErrorAction SilentlyContinue |
    Select-Object ImagePath, Type, Start, ErrorControl | Format-List | Out-String | Write-Host
Write-Host "Enum binding (devices this driver attached to):"
Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Services\applewirelessmouse\Enum" -ErrorAction SilentlyContinue |
    ForEach-Object { Write-Host "  - $($_.PSChildName)" }
if (-not (Test-Path "HKLM:\SYSTEM\CurrentControlSet\Services\applewirelessmouse\Enum")) {
    Write-Host "  (no Enum key yet)"
}

Stop-Transcript | Out-Null
