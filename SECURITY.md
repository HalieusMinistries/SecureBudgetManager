# Security policy

Secure Budget Manager is a **local-only** household finance application for one Windows computer. The household has chosen ordinary local SQLite storage without database encryption, a vault password or a pending-import startup workflow.

## Chosen threat model

This programme keeps household figures off the internet. It does **not** protect those figures from someone who can read the files on this computer.

- The live database is an ordinary SQLite file under `%LocalAppData%\SecureBudgetManager`.
- Local backups, CSV exports and PNG screenshots also contain household financial information.
- Anyone with access to those files may read them.
- The Privacy screen only hides figures while the programme is running. It is not a security boundary.
- Restarting the programme opens the Dashboard normally.
- There is no master password, vault unlock, DPAPI wrapping or SQLCipher encryption in the running design.

Older encrypted recovery files may still exist as unused artefacts. They are not the live store.

## What is written to disk

| File | Contents | Readable? |
| --- | --- | --- |
| `household.sbmdb` | Ordinary SQLite household database | Yes, with ordinary SQLite tools |
| `*.sbmbak` | Local SQLite backup written by `VACUUM INTO` | Yes, with ordinary SQLite tools |
| `*.manifest.json` | Backup SHA-256, size, schema version | No household figures |

Both the live file and backups live under `%LocalAppData%\SecureBudgetManager` unless the user copies them elsewhere. They are not uploaded.

## What the programme still protects

- Saves are transactional. A failed write does not replace the previous successful document.
- Schema migrations are versioned and applied forward-only.
- Corruption detection uses SQLite `integrity_check` and `foreign_key_check`.
- Backups carry a SHA-256 manifest. A hash mismatch or a non-SQLite file is refused.
- Restore keeps a rollback copy of the previous live file until the replacement succeeds.
- Only one instance may run (`Local\SecureBudgetManager.SingleInstance`).
- Logs must not contain secrets, card numbers or household figures.

## Local-only operation

- The programme must run on the user's Windows computer.
- Do not add cloud services, remote backends, online banking, web APIs, update telemetry or any other network access in application code.
- Do not store household data on network shares by default. Backup locations remain under the user's explicit local control.

## No telemetry or external network calls

- Do not include analytics, crash reporters that upload data, advertising SDKs, or Application Insights.
- Diagnostic logging, when added, must stay on the device and must not send events off-machine.
- Third-party packages must be reviewed before adding them so they cannot introduce outbound calls.

## No bank credentials

- Do not collect, store or prompt for bank usernames, passwords, PINs, recovery codes or card details.
- Do not integrate open-banking, statement-scraping or payment-provider APIs.

## Local SQLite store

- The live database uses ordinary SQLite (`SQLitePCLRaw.bundle_e_sqlite3` plus `Microsoft.Data.Sqlite.Core`).
- Do not reintroduce SQLCipher, DPAPI key wrapping or a startup password unless the household explicitly changes this design.
- A leftover encrypted file is refused rather than opened as an empty database.
- Do not describe the live SQLite file or its backups as encrypted.

## Privacy screen

- The Privacy button, inactivity timeout and Windows lock can hide figures on screen.
- This is not database encryption.
- Restarting still opens the Dashboard.
- It does not stop someone who can open `household.sbmdb`.

## Sensitive logging

- Log events are operation names and counts only.
- `SensitiveLogGuard` refuses secrets, card-like numbers and household figures in audit rows.
