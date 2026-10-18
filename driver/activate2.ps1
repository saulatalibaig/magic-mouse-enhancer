# Reference-exact activation: disable/enable the PARENT BTHENUM HID node (elevated)
$ErrorActionPreference = "Continue"
$log = "C:\Users\dir.minister\.asterxa-ai\desktop-workspace\MagicMouseEnhancer\driver\activate2-log.txt"
Start-Transcript -Path $log -Force | Out-Null

Write-Host "=== Activation v2: disable/enable parent node ==="

$parent = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    $_.InstanceId -match "^BTHENUM\\\{00001124-0000-1000-8000-00805f9b34fb\}" -and
    $_.InstanceId -match "004C|05AC" -and $_.Class -eq "HIDClass"
} | Select-Object -First 1

if (-not $parent) { Write-Host "ERROR: parent not found"; Stop-Transcript | Out-Null; exit 1 }
Write-Host "Parent: $($parent.InstanceId)"

# verify LowerFilters present
$reg = "HKLM:\SYSTEM\CurrentControlSet\Enum\$($parent.InstanceId)"
$lf = (Get-ItemProperty $reg -Name LowerFilters -ErrorAction SilentlyContinue).LowerFilters
Write-Host "LowerFilters: $lf"

# stop the manually-started driver first so state is clean
sc.exe stop applewirelessmouse 2>&1 | Out-Null
Start-Sleep -Milliseconds 500

# disable / enable the parent (this forces a full stack rebuild)
Write-Host "Disabling parent..."
Disable-PnpDevice -InstanceId $parent.InstanceId -Confirm:$false -ErrorAction Continue
Start-Sleep -Seconds 3
$st = (Get-PnpDevice -InstanceId $parent.InstanceId -ErrorAction SilentlyContinue).Status
Write-Host "After disable, status: $st"
Write-Host "Enabling parent..."
Enable-PnpDevice -InstanceId $parent.InstanceId -Confirm:$false -ErrorAction Continue
Start-Sleep -Seconds 4
$st = (Get-PnpDevice -InstanceId $parent.InstanceId -ErrorAction SilentlyContinue).Status
Write-Host "After enable, status: $st"

# did the filter service start?
sc.exe query applewirelessmouse | Out-String | Write-Host
$svc = Get-Service applewirelessmouse -ErrorAction SilentlyContinue
Write-Host "Service: $($svc.Status)"

Stop-Transcript | Out-Null
