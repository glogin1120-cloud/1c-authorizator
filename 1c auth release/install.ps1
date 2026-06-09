# Get paths
$installDir = "$env:LOCALAPPDATA\1C_Authorizator"
$oldInstallDir = "C:\Across\1C Authorizator"

Write-Host "========================================================"
Write-Host "Installing 1C Authorizator"
Write-Host "========================================================"
Write-Host ""

# 1. Stop running processes (only for current user)
Write-Host "[1/5] Stopping running processes..."
Stop-Process -Name "1c-auth" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "1c-auth-debug" -Force -ErrorAction SilentlyContinue

# Stop old powershell monitor if running (using PowerShell 2.0 compatible Get-WmiObject)
Get-WmiObject Win32_Process -Filter "Name = 'powershell.exe' AND CommandLine LIKE '%monitor.ps1%'" -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        $_.Terminate() | Out-Null
    } catch {}
}

# 2. Clean up old installation (best effort, no elevation required)
Write-Host "[2/5] Cleaning up old installation leftovers (if possible)..."
try {
    # Try to remove old scheduled task
    schtasks /delete /tn "1C Authorizator" /f 2>$null | Out-Null
} catch {}

try {
    # Try to remove old autostart shortcut of the monitor from Startup folder
    $startupFolder = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"
    if (Test-Path "$startupFolder\1C_Auth_Monitor.lnk") {
        Remove-Item -Path "$startupFolder\1C_Auth_Monitor.lnk" -Force -ErrorAction SilentlyContinue
    }
} catch {}

try {
    # Try to remove old app directory
    if (Test-Path $oldInstallDir) {
        Remove-Item -Path $oldInstallDir -Recurse -Force -ErrorAction SilentlyContinue
    }
} catch {}

# 3. Copy new files to user profile
Write-Host "[3/5] Installing new version to $installDir..."
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

# Get script directory using PowerShell 2.0 compatible invocation path
$scriptPath = $MyInvocation.MyCommand.Path
if (-not $scriptPath) {
    # Fallback if run interactively
    $scriptDir = $pwd.Path
} else {
    $scriptDir = Split-Path -Parent $scriptPath
}

Copy-Item -Path "$scriptDir\1c-auth.exe" -Destination "$installDir\" -Force
Copy-Item -Path "$scriptDir\config.yaml" -Destination "$installDir\" -Force
Copy-Item -Path "$scriptDir\across.ico" -Destination "$installDir\" -Force

# 4. Set up autostart in HKCU Run (runs under current user on logon)
Write-Host "[4/5] Configuring autostart for the current user..."
$registryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Set-ItemProperty -Path $registryPath -Name "1C_Authorizator" -Value "`"$installDir\1c-auth.exe`"" -Force

# 5. Start the application
Write-Host "[5/5] Starting 1C Authorizator..."
Start-Process -FilePath "$installDir\1c-auth.exe" -WorkingDirectory $installDir

Write-Host "========================================================"
Write-Host "DONE! Installation complete."
Write-Host "App path: $installDir\1c-auth.exe"
Write-Host "Autostart is configured via Registry Run key (HKCU)."
Write-Host "========================================================"
Write-Host ""

Read-Host "Press Enter to exit..."
