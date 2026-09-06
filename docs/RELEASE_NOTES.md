# Release notes — 1.0.3

Operational usability and guided-budget release. Existing household records and earlier tags remain unchanged.

- Every overlay Save validates, persists through the session, verifies the stored values, closes the editor and shows a brief confirmation
- Validation failures keep the editor open with a field-level reason; unexpected failures say the information was not saved
- Optional grocery-assistance dates and unknown weekly values save as unknown
- “Create our suggested starting budget” reviews proposals before anything is overwritten
- Known bills keep their recorded amounts; flexible categories use guidance and income
- Covered costs such as parking included in rent and employer-covered tolls stay in the catalogue at $0 household outflow
- Sidebar is grouped into Set up your budget, Use your budget, and Plan and reference
- Incomplete setup opens the next needed step; completed setup opens This Week

# Release notes — 1.0.2

Interaction redesign for everyday record editing. Household calculations and stored records from 1.0.1 are unchanged.

- Shared `EditorOverlay` for household, income, expenses, benefits, bills, grocery categories and assistance, savings, debt, actuals, products, planning, local guidance, international transfers and foreign accounts
- Click selects; double-click, Enter or Edit opens the record immediately
- Save validates and persists; Cancel leaves the record unchanged; unsaved changes are confirmed
- Selection, keyboard focus and scroll position are restored after an editor closes
- Ctrl+S saves the active editor; Escape cancels; Ctrl+Shift+S remains full-page capture
- Privacy mode dismisses open financial editors
- Editors fit a 1366×720 working area
- Bills: assignment, payment, reservation and personal transfers in one overlay; shared-split summary stays read-only on the page
- Grocery assistance opens through Add assistance or Edit; unknown values stay unknown
- International transfers, support commitments and foreign accounts keep recorded rates, balances and fees; nothing unknown is invented

# Release notes — 1.0.1

Operational readiness for separate incomes and bill reservations.

# Release notes — 1.0.0

First complete household-finance release of Secure Budget Manager.

- Local-only SQLite household database with no vault password
- Dashboard opens directly; Privacy screen hides figures on screen only
- Household, income, expenses, payroll, benefits, actuals, cash flow and This Week
- Needs-based allocation, grocery planning and local guidance
- Savings, debt, true-cost planning and international transfers
- Local hashed backups, restore verification and CSV import/export
- Member archive, undo of recent document changes, privacy-safe audit trail
- Four-weekly schedules and weekend/holiday due-date adjustment
- Help glossary, installer/publish packaging and isolated-profile data root
