# Backup and restore

Settings → Create backup writes a local SQLite copy (`.sbmbak`) and a SHA-256 manifest. The backup contains household financial information. Anyone with the file may read it. Store it only in a trusted location.

The programme refuses a backup whose hash, size or SQLite header does not match the manifest.

## Restore

1. Open Settings.
2. Choose Restore backup and pick the `.sbmbak` file.
3. Confirm. The live database is replaced only after the backup validates. A rollback copy is kept until the replacement succeeds.
4. Household data is reloaded immediately.

A leftover encrypted file from an older design is not a restorable local backup.

## CSV export

CSV is a portable plaintext exchange for members, accounts, income names, expenses, transactions, benefits, debts, funds and goals. It contains household financial information and is **not** a database backup.

Not included in CSV (use a local database backup instead):

- Payroll withholding profiles
- Planning scenarios
- International transfers and foreign accounts
- Products, prices and sales-tax rules
- Allocation rules and grocery category detail
- Audit history

## Isolated restore check

Release verification restores a fictional backup into `SECURE_BUDGET_MANAGER_DATA_ROOT`. The real household database is not used for that check.
