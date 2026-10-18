# Finalize: bind the Apple Wireless Mouse filter at startup (runs as SYSTEM via Task Scheduler)
$ErrorActionPreference = "Continue"
$log = "C:\Users\dir.minister\.asterxa-ai\desktop-workspace\MagicMouseEnhancer\driver\finalize-log.txt"
Start-Transcript -Path $log -Force | Out-Null

Write-Host "=== Finalize Apple Wireless Mouse filter binding ==="

# 1. wait for the Bluetooth HID device to appear (BT stack comes up slowly at boot)
$parent = $null
for ($i = 0; $i -lt 30; $i++) {
    $parent = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
        $_.InstanceId -match "^BTHENUM\\\{00001124-0000-1000-8000-00805f9b34fb\}" -and
        $_.InstanceId -match "004C|05AC" -and $_.Class -eq "HIDClass"
    } | Select-Object -First 1
    if ($parent) { break }
    Start-Sleep -Seconds 2
}
if (-not $parent) {
    Write-Host "ERROR: mouse device not found after 60s" -ForegroundColor Red
    Stop-Transcript | Out-Null
    exit 1
}
Write-Host "Found device: $($parent.InstanceId)" -ForegroundColor Green

# 2. make sure the official driver package is in the DriverStore
$published = pnputil.exe /enum-drivers 2>&1 | Out-String
if ($published -match "applewirelessmouse") {
    Write-Host "Driver package present in DriverStore." -ForegroundColor Green
} else {
    Write-Host "Re-adding driver package..." -ForegroundColor Yellow
    $inf = "C:\Users\dir.minister\.asterxa-ai\desktop-workspace\MagicMouseEnhancer\driver\apple-official\extracted\AppleWirelessMouse\AppleWirelessMouse.inf"
    pnputil.exe /add-driver $inf /install 2>&1 | Out-String | Write-Host
}

# 3. force the device to use the Apple driver package (oem*.inf) via update-device
Write-Host "Updating device driver to Apple package..." -ForegroundColor Yellow
pnputil.exe /update-device "$($parent.InstanceId)" 2>&1 | Out-String | Write-Host

# 4. restart the device to rebuild the stack with the filter
Write-Host "Restarting device..." -ForegroundColor Yellow
pnputil.exe /restart-device "$($parent.InstanceId)" 2>&1 | Out-String | Write-Host
Start-Sleep -Seconds 5

# 5. report final state
Write-Host "=== FINAL STATE ===" -ForegroundColor Cyan
sc.exe query applewirelessmouse 2>&1 | Out-String | Write-Host
$svc = Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\applewirelessmouse" -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "Service config: Start=$($svc.Start) Type=$($svc.Type) ErrorControl=$($svc.ErrorControl)"
    Write-Host "Enum bindings:"
    Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Services\applewirelessmouse\Enum" -ErrorAction SilentlyContinue |
        ForEach-Object { Write-Host "  - $($_.PSChildName)" }
} else {
    Write-Host "Service not present yet."
}
pnputil.exe /enum-devices /instanceid "$($parent.InstanceId)" /drivers 2>&1 | Out-String | Write-Host

Stop-Transcript | Out-Null
