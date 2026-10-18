# ============================================================================
#  Optional: enable Magic Mouse Bluetooth touch on Windows
#  (MagicMouseEnhancer companion script - run as Administrator)
# ============================================================================
#  WHY THIS EXISTS
#  Windows' Bluetooth HID stack (hidbth) refuses HID feature reports from
#  user-mode applications (ERROR_INVALID_FUNCTION, win32 error 1). The Magic
#  Mouse 2 only streams touch data after receiving the F1 02 01 feature
#  report, so a user-mode app alone cannot enable touch over Bluetooth.
#  Apple's own signed driver (applewirelessmouse.sys, from Boot Camp /
#  Apple Software Update) is a HID lower filter that CAN switch the device
#  into multitouch mode from kernel mode. Installing it as a LowerFilter on
#  the mouse's HID node is the proven free fix (same method as the
#  apple-magic-mouse-scroll-fix-windows project).
#
#  REQUIREMENTS
#  - Run as Administrator
#  - applewirelessmouse.sys on disk somewhere (DriverStore, Boot Camp
#    drivers, Apple Software Update, or the driver\ folder next to this
#    script). If missing, install "Apple Software Update" from
#    https://support.apple.com/en-us/106349 or use a Boot Camp drivers zip.
#
#  AFTER RUNNING
#  The Magic Mouse should switch into multitouch mode over Bluetooth.
#  MagicMouseEnhancer (running in the tray) will then read the touch data
#  and provide scrolling + gestures. Restart the app if it was already open.
# ============================================================================

$ErrorActionPreference = "Stop"

# Self-log: capture everything to a file next to the script
$logFile = Join-Path $PSScriptRoot "driver-install-log.txt"
Start-Transcript -Path $logFile -Force | Out-Null

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: Run this script as Administrator." -ForegroundColor Red
    Stop-Transcript | Out-Null
    exit 1
}

Write-Host ""
Write-Host "  Magic Mouse Bluetooth touch - driver installer" -ForegroundColor Cyan
Write-Host "  ===============================================" -ForegroundColor Cyan

# -- 1. find the mouse's HID node -------------------------------------------
Write-Host "[1/4] Locating Magic Mouse Bluetooth HID device..." -ForegroundColor Yellow
# The touch surface lives on the vendor-defined HID collection (COL02+).
# Instance ID looks like: HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004C_PID&0269&COL02\...
$mouse = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    ($_.InstanceId -match "00001124-0000-1000-8000-00805f9b34fb") -and
    ($_.InstanceId -match "004C|05AC") -and
    ($_.InstanceId -match "COL0[2-9]") -and
    $_.Class -eq "HIDClass"
} | Select-Object -First 1

if (-not $mouse) {
    # Fallback: any HID-class Apple BT node
    $mouse = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
        ($_.InstanceId -match "BTHENUM|00001124-0000-1000-8000-00805f9b34fb") -and
        ($_.InstanceId -match "004C|05AC") -and
        $_.Class -eq "HIDClass"
    } | Select-Object -First 1
}

if (-not $mouse) {
    Write-Host "  ERROR: Magic Mouse HID device not found. Pair it first." -ForegroundColor Red
    Stop-Transcript | Out-Null
    exit 1
}
Write-Host "  Found: $($mouse.FriendlyName)" -ForegroundColor Green
Write-Host "  ID:    $($mouse.InstanceId)" -ForegroundColor DarkGray

# -- 2. locate applewirelessmouse.sys ----------------------------------------
Write-Host "[2/4] Locating applewirelessmouse.sys..." -ForegroundColor Yellow
$dest = "$env:SystemRoot\System32\drivers\applewirelessmouse.sys"
$src  = $null

if (Test-Path $dest) { $src = $dest }
if (-not $src) {
    $bundled = Join-Path $PSScriptRoot "driver\applewirelessmouse.sys"
    if (Test-Path $bundled) { $src = $bundled }
}
if (-not $src) {
    $store = Get-ChildItem "$env:SystemRoot\System32\DriverStore\FileRepository" -Filter "applewirelessmouse.sys" -Recurse -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($store) { $src = $store.FullName }
}
if (-not $src) {
    foreach ($p in @("$env:ProgramFiles\Boot Camp\Drivers\Apple", "$env:ProgramFiles(x86)\Boot Camp\Drivers\Apple",
                     "$env:ProgramData\Apple\Installer Cache", "$env:SystemRoot\System32\DriverStore")) {
        $f = Get-ChildItem $p -Filter "applewirelessmouse.sys" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($f) { $src = $f.FullName; break }
    }
}
if (-not $src) {
    Write-Host "  ERROR: applewirelessmouse.sys not found." -ForegroundColor Red
    Write-Host "  Install 'Apple Software Update' (https://support.apple.com/en-us/106349)" -ForegroundColor Yellow
    Write-Host "  or place the driver file in a 'driver' folder next to this script, then re-run." -ForegroundColor Yellow
    Stop-Transcript | Out-Null
    exit 1
}
Write-Host "  Driver: $src" -ForegroundColor Green
if ($src -ne $dest) { Copy-Item $src $dest -Force; Write-Host "  Copied to $dest" -ForegroundColor Green }

# -- 3. create the kernel service ---------------------------------------------
Write-Host "[3/4] Creating kernel service..." -ForegroundColor Yellow
if (-not (Get-Service -Name "applewirelessmouse" -ErrorAction SilentlyContinue)) {
    sc.exe create applewirelessmouse type= kernel start= demand error= ignore binPath= "System32\drivers\applewirelessmouse.sys" DisplayName= "Apple Wireless Mouse" | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "  ERROR: sc.exe create failed ($LASTEXITCODE)" -ForegroundColor Red; Stop-Transcript | Out-Null; exit 1 }
}
Write-Host "  Service ready." -ForegroundColor Green

# -- 4. register as LowerFilter on the device ----------------------------------
Write-Host "[4/4] Registering LowerFilter..." -ForegroundColor Yellow
$devPath = "HKLM:\SYSTEM\CurrentControlSet\Enum\$($mouse.InstanceId)"
if (-not (Test-Path $devPath)) { Write-Host "  ERROR: device registry key missing" -ForegroundColor Red; Stop-Transcript | Out-Null; exit 1 }
$filters = @((Get-ItemProperty $devPath -Name LowerFilters -ErrorAction SilentlyContinue).LowerFilters)
if ($filters -notcontains "applewirelessmouse") {
    $filters = @($filters) + @("applewirelessmouse")
    Set-ItemProperty $devPath -Name LowerFilters -Value $filters -Type MultiString
    Write-Host "  LowerFilter registered." -ForegroundColor Green
} else {
    Write-Host "  LowerFilter already present." -ForegroundColor Green
}

# -- restart the device --------------------------------------------------------
try {
    Disable-PnpDevice -InstanceId $mouse.InstanceId -Confirm:$false -ErrorAction Stop
    Start-Sleep -Seconds 2
    Enable-PnpDevice -InstanceId $mouse.InstanceId -Confirm:$false -ErrorAction Stop
    Write-Host "  Device restarted." -ForegroundColor Green
} catch {
    Write-Host "  Reboot required to activate the driver." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "  Done. Restart MagicMouseEnhancer if it is running." -ForegroundColor Cyan
Write-Host ""
Stop-Transcript | Out-Null
