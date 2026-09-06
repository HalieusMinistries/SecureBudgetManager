# Installation

## Release package

The supported Windows package is a **self-contained win-x64** publish of `SecureBudgetManager.exe`. It does not require a separate .NET runtime and does not depend on this source tree or a Debug folder.

Build it:

```powershell
powershell -File packaging\Build-Release.ps1
```

Output:

- Published files: `src\SecureBudgetManager.App\bin\Release\net8.0-windows\publish\win-x64\SecureBudgetManager.exe`
- Installer (when Inno Setup 6 is installed): `dist\SecureBudgetManager-1.0.3-win-x64.exe`
- Portable zip (when Inno Setup is not installed): `dist\SecureBudgetManager-1.0.3-win-x64.zip`

## Installer behaviour

- Product name: Secure Budget Manager
- Publisher: Secure Budget Manager
- Version: 1.0.3
- Upgrade code: `{8C3F0A61-2E47-4B9A-9D11-6A5F2C8E1B70}`
- Start-menu shortcut is created
- Desktop shortcut is optional
- Add/Remove Programs entry is created
- Uninstall keeps `%LocalAppData%\SecureBudgetManager` and `Backups` unless the user explicitly removes them

PowerShell install without Inno Setup:

```powershell
powershell -File packaging\Install-SecureBudgetManager.ps1
powershell -File packaging\Install-SecureBudgetManager.ps1 -DesktopShortcut
```

## Upgrade

Installing 1.0.3 over an earlier copy of the same upgrade code replaces programme files and leaves the local household database in place. The programme applies any pending schema migrations. When minimum setup is complete it opens This Week; otherwise it opens the next incomplete setup step.

## Isolated test profile

```powershell
$env:SECURE_BUDGET_MANAGER_DATA_ROOT = "$env:TEMP\SecureBudgetManager-isolated"
```

The real household database in `%LocalAppData%\SecureBudgetManager` is not used while that variable is set.

## Code signing

If a code-signing certificate is available:

```powershell
powershell -File packaging\Sign-Release.ps1 -Path <exe-or-installer>
```

If no certificate exists, the rest of the installer still builds. Signing is the only certificate-dependent step.
