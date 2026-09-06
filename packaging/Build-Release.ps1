#Requires -Version 5.1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$publishDir = Join-Path $root "src\SecureBudgetManager.App\bin\Release\net8.0-windows\publish\win-x64"
$distDir = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

Write-Host "Publishing self-contained win-x64 Release..."
dotnet publish "src\SecureBudgetManager.App\SecureBudgetManager.App.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$exe = Join-Path $publishDir "SecureBudgetManager.exe"
if (-not (Test-Path $exe)) { throw "Published SecureBudgetManager.exe was not found." }

$signingScript = Join-Path $PSScriptRoot "Sign-Release.ps1"
if (Test-Path $signingScript) {
    & $signingScript -Path $exe
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $resolved = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($resolved) { $iscc = $resolved.Source }
}

$installer = Join-Path $distDir "SecureBudgetManager-1.0.1-win-x64.exe"
if ($iscc) {
    Write-Host "Building Inno Setup installer..."
    & $iscc (Join-Path $PSScriptRoot "SecureBudgetManager.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }
} else {
    Write-Host "Inno Setup is not installed. Creating a portable zip and PowerShell installer payload."
    $zip = Join-Path $distDir "SecureBudgetManager-1.0.1-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip
    Copy-Item (Join-Path $PSScriptRoot "Install-SecureBudgetManager.ps1") (Join-Path $distDir "Install-SecureBudgetManager.ps1") -Force
    Write-Host "Portable package: $zip"
}

Write-Host "Release output is in $distDir"
Write-Host "Published executable: $exe"
if (Test-Path $installer) { Write-Host "Installer: $installer" }
