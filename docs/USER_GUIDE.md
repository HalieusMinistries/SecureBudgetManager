# User guide

Secure Budget Manager is a local-only household finance book for Windows. It helps a household see what money is arriving, what must be protected, and what is safe to spend. It does not give legal, tax, immigration or investment advice.

## First run

1. Install or launch `SecureBudgetManager.exe`.
2. The programme opens directly to the Dashboard. There is no vault password and no unlock screen.

Household figures stay in a local SQLite file on this computer. Anyone with access to that file may read them. There is no online account.

## Everyday use

- **This Week** answers what may be spent now, what must be reserved, and each eligible person's personal allowance.
- **Dashboard** summarises income, essentials, optional spending and debt.
- **Household** records members. Discretionary eligibility is never inferred. Dependants can be included in needs without receiving an envelope.
- **Income** records wages, salary, variable pay, reimbursements and one-off amounts. Reimbursements stay out of ordinary disposable income.
- **Expenses** records bills with real due dates. A month is never treated as four weeks.
- **Payroll** and **Benefits** record withholding, 401(k), Roth, employer match and employee benefits. Unconfirmed benefits are planned, not applied historically.
- **Actuals** compares estimates with payslips and spent amounts.
- **Cash Flow** shows daily balances, confirmed and projected paydays, and the lowest balance.
- **Allocations** and **Allocation Rules** apply the needs hierarchy and payday reservations.
- **Grocery Plan** keeps the current assisted plan and the fallback plan if assistance stops.
- **Local Guidance** stores sourced, dated cost figures. Missing local prices stay missing.
- **Savings**, **Debt**, **Planning** and **International Transfers** cover sinking funds, payoff order, true-cost scenarios and USD/ZAR transfers.
- **Settings** covers the privacy timeout, local backup/restore and CSV import/export.
- **Help and About** explains American household-finance terms in plain English.

## Screenshots, CSV and backups

Ctrl+S captures the current page as a PNG. CSV export writes a portable text file. Local database backups write a SQLite copy plus a hash manifest.

All three contain household financial information. Anyone with those files may read them. Store them only in a trusted location. None of them is encrypted.

## Privacy screen

The Privacy button, inactivity timeout and Windows lock can hide figures on screen. This is not database encryption. Restarting the programme opens the Dashboard normally. It is not a security boundary against someone who can open the Windows files.
