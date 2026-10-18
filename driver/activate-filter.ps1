# Activate applewirelessmouse filter on the correct device node (elevated)
$ErrorActionPreference = "Stop"
$log = "C:\Users\dir.minister\.asterxa-ai\desktop-workspace\MagicMouseEnhancer\driver\activate-log.txt"
Start-Transcript -Path $log -Force | Out-Null

Write-Host "=== Activate applewirelessmouse filter ==="

# The parent BT HID device (BTHENUM\{00001124-...}) - this is where the reference
# project registers the LowerFilter. Also keep the COL02 interface covered.
$parent = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    $_.InstanceId -match "^BTHENUM\\\{00001124-0000-1000-8000-00805f9b34fb\}" -and
    $_.InstanceId -match "004C|05AC"
} | Select-Object -First 1

if (-not $parent) {
    Write-Host "ERROR: BTHENUM parent node not found" -ForegroundColor Red
    Stop-Transcript | Out-Null
    exit 1
}
$parentId = $parent.InstanceId
Write-Host "Parent node: $parentId" -ForegroundColor Green

# Add LowerFilters to the parent node
$parentReg = "HKLM:\SYSTEM\CurrentControlSet\Enum\$parentId"
$pf = @((Get-ItemProperty $parentReg -Name LowerFilters -ErrorAction SilentlyContinue).LowerFilters)
if ($pf -notcontains "applewirelessmouse") {
    $pf = @($pf) + @("applewirelessmouse")
    Set-ItemProperty $parentReg -Name LowerFilters -Value $pf -Type MultiString
    Write-Host "LowerFilter added to parent node" -ForegroundColor Green
} else {
    Write-Host "Parent already has LowerFilter" -ForegroundColor DarkGray
}

# Also cover COL02 (vendor-defined touch collection) - already set, verify
$col02 = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    $_.InstanceId -match "00001124-0000-1000-8000-00805f9b34fb" -and
    $_.InstanceId -match "COL02" -and $_.Class -eq "HIDClass"
} | Select-Object -First 1
if ($col02) {
    $r = "HKLM:\SYSTEM\CurrentControlSet\Enum\$($col02.InstanceId)"
    $f = @((Get-ItemProperty $r -Name LowerFilters -ErrorAction SilentlyContinue).LowerFilters)
    if ($f -notcontains "applewirelessmouse") {
        $f = @($f) + @("applewirelessmouse")
        Set-ItemProperty $r -Name LowerFilters -Value $f -Type MultiString
        Write-Host "LowerFilter added to COL02" -ForegroundColor Green
    } else {
        Write-Host "COL02 already has LowerFilter" -ForegroundColor DarkGray
    }
}

# Force a REAL stack rebuild with pnputil (disable/enable was not enough)
Write-Host "Restarting device stack with pnputil..." -ForegroundColor Yellow
& pnputil.exe /restart-device "$parentId"
Write-Host "pnputil parent exit: $LASTEXITCODE" -ForegroundColor Green

if ($col02) {
    & pnputil.exe /restart-device "$($col02.InstanceId)"
    Write-Host "pnputil col02 exit: $LASTEXITCODE" -ForegroundColor Green
}

Start-Sleep -Seconds 3

# Report state
$svc = Get-Service applewirelessmouse -ErrorAction SilentlyContinue
Write-Host "Service state: $($svc.Status) / $($svc.StartType)" -ForegroundColor Cyan
sc.exe query applewirelessmouse | Out-String | Write-Host

Stop-Transcript | Out-Null
