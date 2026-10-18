# Proper install of Apple's INF driver package for the Magic Mouse 2 (elevated)
$ErrorActionPreference = "Continue"
$log = "C:\Users\dir.minister\.asterxa-ai\desktop-workspace\MagicMouseEnhancer\driver\proper-install-log.txt"
Start-Transcript -Path $log -Force | Out-Null

Write-Host "=== Proper driver package install ==="

# 0. clean up manual LowerFilters + service from earlier attempts so the
#    INF-based install owns the binding (no conflicts)
$nodes = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    $_.InstanceId -match "00001124-0000-1000-8000-00805f9b34fb" -and $_.InstanceId -match "004C|05AC"
}
foreach ($n in $nodes) {
    $reg = "HKLM:\SYSTEM\CurrentControlSet\Enum\$($n.InstanceId)"
    if (Get-ItemProperty $reg -Name LowerFilters -ErrorAction SilentlyContinue) {
        Remove-ItemProperty $reg -Name LowerFilters -ErrorAction SilentlyContinue
        Write-Host "Removed manual LowerFilters from $($n.InstanceId)" -ForegroundColor Yellow
    }
}
if (Get-Service applewirelessmouse -ErrorAction SilentlyContinue) {
    sc.exe stop applewirelessmouse 2>&1 | Out-Null
    sc.exe delete applewirelessmouse 2>&1 | Out-Null
    Write-Host "Removed manually-created service" -ForegroundColor Yellow
}
Start-Sleep -Milliseconds 500

# 1. install the driver package into the DriverStore (with catalog + INF)
$inf = "C:\Users\dir.minister\.asterxa-ai\desktop-workspace\MagicMouseEnhancer\driver\mm2win11\AppleWirelessMouse.inf"
Write-Host "Adding driver package via pnputil (this is the real install)..." -ForegroundColor Yellow
pnputil.exe /add-driver $inf /install 2>&1 | Out-String | Write-Host

# 2. find the BTHENUM parent node and restart it so PnP binds the filter
$parent = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    $_.InstanceId -match "^BTHENUM\\\{00001124-0000-1000-8000-00805f9b34fb\}" -and
    $_.InstanceId -match "004C|05AC" -and $_.Class -eq "HIDClass"
} | Select-Object -First 1
if ($parent) {
    Write-Host "Restarting device: $($parent.InstanceId)" -ForegroundColor Yellow
    pnputil.exe /restart-device "$($parent.InstanceId)" 2>&1 | Out-String | Write-Host
}
Start-Sleep -Seconds 3

# 3. report
Write-Host "=== State after install ===" -ForegroundColor Cyan
sc.exe query applewirelessmouse 2>&1 | Out-String | Write-Host
Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\applewirelessmouse" -ErrorAction SilentlyContinue |
    Select-Object ImagePath, Type, Start, ErrorControl | Format-List | Out-String | Write-Host
Write-Host "Enum binding:" -ForegroundColor Cyan
Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Services\applewirelessmouse\Enum" -ErrorAction SilentlyContinue |
    ForEach-Object { Write-Host "  - $($_.PSChildName)" }

Stop-Transcript | Out-Null
