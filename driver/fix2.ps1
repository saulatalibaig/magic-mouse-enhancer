# Revert to demand start + rebuild stack + verify attachment (elevated)
$ErrorActionPreference = "Continue"
$log = "C:\Users\dir.minister\.asterxa-ai\desktop-workspace\MagicMouseEnhancer\driver\fix2log.txt"
Start-Transcript -Path $log -Force | Out-Null

Write-Host "=== Revert to demand start, rebuild, verify ==="

sc.exe config applewirelessmouse start= demand error= normal | Out-String | Write-Host
Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\applewirelessmouse" | Select-Object Start, ErrorControl | Format-List | Out-String | Write-Host

sc.exe stop applewirelessmouse 2>&1 | Out-Null
Start-Sleep -Milliseconds 500

$parent = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    $_.InstanceId -match "^BTHENUM\\\{00001124-0000-1000-8000-00805f9b34fb\}" -and
    $_.InstanceId -match "004C|05AC" -and $_.Class -eq "HIDClass"
} | Select-Object -First 1
if (-not $parent) { Write-Host "ERROR: parent not found"; Stop-Transcript | Out-Null; exit 1 }
Write-Host "Parent: $($parent.InstanceId)"

Disable-PnpDevice -InstanceId $parent.InstanceId -Confirm:$false -ErrorAction Continue
Start-Sleep -Seconds 3
Enable-PnpDevice -InstanceId $parent.InstanceId -Confirm:$false -ErrorAction Continue
Start-Sleep -Seconds 4

sc.exe query applewirelessmouse | Out-String | Write-Host

# Verify whether the filter is ACTUALLY in the device driver stack
Write-Host "=== Driver stack after rebuild ==="
pnputil.exe /enum-devices /instanceid "$($parent.InstanceId)" /drivers 2>&1 | Out-String | Write-Host

Stop-Transcript | Out-Null
