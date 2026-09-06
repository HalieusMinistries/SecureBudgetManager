#Requires -Version 5.1
param(
    [switch]$RemoveHouseholdData,
    [switch]$RemoveBackups
)

$ErrorActionPreference = "Stop"
$install = Split-Path -Parent $MyInvocation.MyCommand.Path
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Secure Budget Manager"
$desktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "Secure Budget Manager.lnk"
$arp = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SecureBudgetManager"

Get-Process -Name SecureBudgetManager -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.Path -and $_.Path.StartsWith($install, [System.StringComparison]::OrdinalIgnoreCase)) {
        Stop-Process -Id $_.Id -Force
    }
}

if (Test-Path $startMenu) { Remove-Item $startMenu -Recurse -Force }
if (Test-Path $desktop) { Remove-Item $desktop -Force }
if (Test-Path $arp) { Remove-Item $arp -Recurse -Force }

Get-ChildItem $install -Force | Where-Object { $_.Name -ne "Uninstall-SecureBudgetManager.ps1" } | Remove-Item -Recurse -Force

$data = Join-Path $env:LOCALAPPDATA "SecureBudgetManager"
if ($RemoveHouseholdData -and (Test-Path $data)) {
    if (-not $RemoveBackups) {
        $backup = Join-Path $data "Backups"
        $keep = Join-Path $env:LOCALAPPDATA "SecureBudgetManager-preserved-backups"
        if (Test-Path $backup) {
            New-Item -ItemType Directory -Force -Path $keep | Out-Null
            Copy-Item (Join-Path $backup "*") $keep -Recurse -Force
        }
    }

    Remove-Item $data -Recurse -Force
}

Write-Host "Secure Budget Manager was uninstalled."
if (-not $RemoveHouseholdData) {
    Write-Host "Household data was preserved at $data"
}
