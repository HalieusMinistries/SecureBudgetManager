# Secure Budget Manager

Local-only Windows household finance software. Income, expenses and cash-flow figures stay on this computer in an ordinary SQLite database. There is no vault password, no cloud copy and no database encryption.

The programme shows the real cost of a decision, not only the advertised payment. It does not give legal, tax, immigration or investment advice.

**Version 1.0.3** — see [RELEASE_READINESS.md](RELEASE_READINESS.md) and [docs/RELEASE_NOTES.md](docs/RELEASE_NOTES.md).

## What it does

Household, income, expenses, payroll, benefits, actuals, cash flow, This Week, needs-based allocations, grocery planning, local guidance, savings, debt, true-cost planning, international transfers, local backup/restore, CSV import/export, and Help.

## Architecture

```
SecureBudgetManager.sln
├── src/SecureBudgetManager.App             WPF UI
├── src/SecureBudgetManager.Core            Domain rules and contracts
├── src/SecureBudgetManager.Infrastructure  Local SQLite and backups
└── tests/                                  xUnit tests
```

## Storage

`%LocalAppData%\SecureBudgetManager`

| File | Purpose |
| --- | --- |
| `household.sbmdb` | Ordinary local SQLite household database |
| `Backups/` | Local SQLite backups with hash manifests |

Anyone with access to those files may read household financial information. Store backups only in a trusted location.

The programme opens directly to the Dashboard. The Privacy screen only hides figures while the programme is running. It is not database encryption, and restarting opens the Dashboard normally.

## Build and test

```powershell
dotnet clean SecureBudgetManager.sln
dotnet build SecureBudgetManager.sln
$env:DOTNET_GCServer = "0"
dotnet test SecureBudgetManager.sln -m:1
```

## Release

```powershell
powershell -File packaging\Build-Release.ps1
```

The installed executable is `SecureBudgetManager.exe`. See [docs/INSTALLATION.md](docs/INSTALLATION.md).

## Guides

- [User guide](docs/USER_GUIDE.md)
- [Installation](docs/INSTALLATION.md)
- [Backup and restore](docs/BACKUP_RESTORE.md)
- [Privacy](docs/PRIVACY.md)
- [Security](SECURITY.md)
- [Manual end-to-end test](docs/MANUAL_END_TO_END_TEST.md)
- [Known limitations](docs/KNOWN_LIMITATIONS.md)
- [Developer instructions](docs/DEVELOPER.md)
