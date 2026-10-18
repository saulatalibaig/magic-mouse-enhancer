$ErrorActionPreference = "Stop"
$mouse = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    ($_.InstanceId -match "BTHENUM|00001124-0000-1000-8000-00805f9b34fb") -and
    ($_.InstanceId -match "004C|05AC") -and
    $_.Class -eq "HIDClass"
} | Select-Object -First 1
Write-Output "ok"
