#Requires -Version 5.1
param(
    [string]$PublishDirectory,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA "Programs\Secure Budget Manager"),
    [switch]$DesktopShortcut,
    [switch]$RemoveHouseholdData
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $PublishDirectory) {
    $PublishDirectory = Join-Path $root "src\SecureBudgetManager.App\bin\Release\net8.0-windows\publish\win-x64"
}

$exeName = "SecureBudgetManager.exe"
$sourceExe = Join-Path $PublishDirectory $exeName
if (-not (Test-Path $sourceExe)) {
    throw "Published files were not found at $PublishDirectory. Run packaging\\Build-Release.ps1 first."
}

New-Item -ItemType Directory -Force -Path $InstallDirectory | Out-Null
Copy-Item -Path (Join-Path $PublishDirectory "*") -Destination $InstallDirectory -Recurse -Force

$installedExe = Join-Path $InstallDirectory $exeName
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Secure Budget Manager"
New-Item -ItemType Directory -Force -Path $startMenu | Out-Null

$ws = New-Object -ComObject WScript.Shell
$shortcut = $ws.CreateShortcut((Join-Path $startMenu "Secure Budget Manager.lnk"))
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $InstallDirectory
$shortcut.IconLocation = $installedExe
$shortcut.Save()

if ($DesktopShortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $desk = $ws.CreateShortcut((Join-Path $desktop "Secure Budget Manager.lnk"))
    $desk.TargetPath = $installedExe
    $desk.WorkingDirectory = $InstallDirectory
    $desk.IconLocation = $installedExe
    $desk.Save()
}

$uninstall = Join-Path $InstallDirectory "Uninstall-SecureBudgetManager.ps1"
Copy-Item (Join-Path $PSScriptRoot "Uninstall-SecureBudgetManager.ps1") $uninstall -Force

$arp = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SecureBudgetManager"
New-Item -Path $arp -Force | Out-Null
Set-ItemProperty $arp DisplayName "Secure Budget Manager"
Set-ItemProperty $arp Publisher "Secure Budget Manager"
Set-ItemProperty $arp DisplayVersion "1.0.4"
Set-ItemProperty $arp DisplayIcon $installedExe
Set-ItemProperty $arp InstallLocation $InstallDirectory
Set-ItemProperty $arp UninstallString "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstall`""
Set-ItemProperty $arp NoModify 1
Set-ItemProperty $arp NoRepair 0

if ($RemoveHouseholdData) {
    $data = Join-Path $env:LOCALAPPDATA "SecureBudgetManager"
    if (Test-Path $data) {
        Remove-Item $data -Recurse -Force
    }
}

Write-Host "Installed SecureBudgetManager.exe to $installedExe"
Write-Host "Household data remains in %LocalAppData%\SecureBudgetManager unless -RemoveHouseholdData was used."
