# Fix: make applewirelessmouse a SYSTEM-start driver (like Apple's INF does), rebuild stack (elevated)
$ErrorActionPreference = "Continue"
$log = "C:\Users\dir.minister\.asterxa-ai\desktop-workspace\MagicMouseEnhancer\driver\fixlog.txt"
Start-Transcript -Path $log -Force | Out-Null

Write-Host "=== Fix driver start type + rebuild ==="

# 1. set Start=1 (SERVICE_SYSTEM_START), ErrorControl=1 (normal)
sc.exe config applewirelessmouse start= system error= normal | Out-String | Write-Host
Start-Sleep -Milliseconds 500
Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\applewirelessmouse" | Select-Object ImagePath, Type, Start, ErrorControl | Format-List | Out-String | Write-Host

# 2. find the BTHENUM parent node
$parent = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    $_.InstanceId -match "^BTHENUM\\\{00001124-0000-1000-8000-00805f9b34fb\}" -and
    $_.InstanceId -match "004C|05AC" -and $_.Class -eq "HIDClass"
} | Select-Object -First 1
if (-not $parent) { Write-Host "ERROR: parent node not found"; Stop-Transcript | Out-Null; exit 1 }
Write-Host "Parent: $($parent.InstanceId)"

# 3. rebuild the device stack (disable/enable) so PnP loads the filter now
Disable-PnpDevice -InstanceId $parent.InstanceId -Confirm:$false -ErrorAction Continue
Start-Sleep -Seconds 3
Enable-PnpDevice -InstanceId $parent.InstanceId -Confirm:$false -ErrorAction Continue
Start-Sleep -Seconds 4

# 4. report
sc.exe query applewirelessmouse | Out-String | Write-Host
$svc = Get-Service applewirelessmouse -ErrorAction SilentlyContinue
Write-Host "Service: $($svc.Status) / start=$($svc.StartType)"
Stop-Transcript | Out-Null
