# Developer build and test

```powershell
# Stop only this repository's running executable first.
dotnet clean SecureBudgetManager.sln
dotnet build SecureBudgetManager.sln
$env:DOTNET_GCServer = "0"
dotnet test SecureBudgetManager.sln -m:1
```

Do not use `--no-build` for official validation. Treat MSB3026 copy-retry warnings as failure.

Release publish:

```powershell
powershell -File packaging\Build-Release.ps1
```

Isolated data root for tests that must not touch the live household database:

```powershell
$env:SECURE_BUDGET_MANAGER_DATA_ROOT = "$env:TEMP\SecureBudgetManager-isolated"
```

The solution treats warnings as errors. Core must remain free of WPF and SQLite references.
