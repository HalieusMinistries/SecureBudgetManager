# Release readiness

Version **1.0.4**. This table traces the supplied specification to the implemented programme. v1.0.4 is a hotfix so overlay editors render their forms. v1.0.3 standardises overlay saving, adds a reviewable suggested starting budget, treats covered costs as zero household outflow, and orders navigation by setup dependency. Stored household records remain in place except where the household later saves an editor, suggestion or coverage change.

| Requirement | Implementation | Screen | Domain/application service | Persistence | Automated test | Programmatic verification | Manual verification | Final status | Limitation or external blocker |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Household members, eligibility, archive | `HouseholdMember` with discretionary flag, shared-cost flag, notes, effective dates, archive | Household | `HouseholdViewModel`, `BudgetDocument` | `household_member` v19 | `MemberArchiveTests`, `HouseholdViewModelTests` | Repository round-trip | Confirm names locally | Complete | Real names supplied only at live import |
| Income kinds, hours, OT, reimbursements, unconfirmed paydays | `HourlyIncome`, `SalaryIncome`, `VariableIncome`, `MileageReimbursement`, `IncomeRole`, start/end, `PayDates()` | Income | `EarnerIncomeCalculator`, `IncomeReconciler` | `income_source` | `HouseholdImportTests`, `IncomeViewModelTests` | Dry-run import | Confirm hours locally | Complete | Unknown commute miles remain unknown |
| Expenses, frequencies, due dates | `ExpenseItem`, `Frequency` including four-weekly, `DueDateAdjustment` | Expenses | `RecurrenceSchedule`, `FrequencyConverter` | `expense_item` | `FourWeeklyAndBusinessDayTests`, `RecurrenceScheduleTests` | Monthly conversion checks | Confirm utility dates | Complete | Some due dates are still unknown |
| Payroll and benefits | `PayrollCalculator`, `BenefitPlan.IsConfirmed` | Payroll / Benefits nav | `TakeHomeCalculator` | `payroll_profile`, `benefit_plan` | `PayrollCalculatorTests` | Payslip compare | Confirm benefit start | Complete | Unconfirmed benefits stay planned |
| Versioned tax | `TaxYearLibrary`, Utah Publication 14, NRA W-4 | Payroll | `UtahWithholding`, `W4Settings` | Profile + payslip versions | `TaxRuleVersionTests` | Rule-year selection | Confirm W-4 locally | Complete | Years after 2026 not tabulated |
| Product taxes | `SalesTaxRule`, `CheckoutPricer` | Products | `ProductCatalog` | `sales_tax_rule` | `ProductCatalogTests` | Inclusive vs added | Override locally | Complete | Missing local rates stay missing |
| International transfers | USD/ZAR quotes, fees, purpose | International Transfers | `InternationalTransfers` | transfer tables v17 | `InternationalTransferTests` | Rate not hard-coded | Record actual rate | Complete | Tax treatment remains user-noted |
| Needs hierarchy and payday allocation | `NeedsHierarchy`, `PaychequeAllocator` | This Week, Allocations, Allocation Rules | `ReservationPlanner` | `allocation_rules` | `ReservationAndAllocationTests` | Shortfall remains visible | Live payday check | Complete | Discretionary % may still be unconfigured |
| Grocery and guidance | `GroceryPlanner`, `StarterGuidancePack` | Grocery Plan, Local Guidance | `CostGuidance` | grocery + guidance tables | `StarterGuidancePackTests` | Offline pack version | Confirm assistance | Complete | Utah city totals may be missing |
| Savings and sinking funds | `SavingsFund`, `SavingsAllocator` | Savings | `GoalPlanner` | `savings_fund` | `GoalAndSavingsTests` | Contribution math | Set targets locally | Complete | Dollar targets may be $0 until chosen |
| Debt | `DebtAccount`, `PayoffPlanner` | Debt | Snowball/avalanche | `debt_account` | `PayoffPlannerTests` | 0% APR path | Confirm the imported debt row | Complete | Live balance confirmed at import |
| Planning / true cost | `CarPurchasePlanner`, `AffordabilityEngine` | Planning | `TrueCostEngine` | `scenario` | `CarPurchasePlannerTests` | Advertised ≠ true cost | Try $235 example | Complete | None |
| Actuals and cash flow | Transactions, payslips, `CashFlowProjector` | Actuals, Cash Flow | `ExpenseReconciler` | transactions, payslips | `CashFlowProjectorTests` | Historical slips not replayed | Enter later actuals | Complete | None |
| Settings CSV import | User-directed preview and merge | Settings | `BudgetCsvExchange` | Local save after confirm | CSV tests | No startup pending file | Import a fictional CSV | Complete | One-time household seed is outside the app |
| Security, privacy, backup | Local SQLite, privacy screen, hashed backups | Settings, Privacy | `VaultService`, `EncryptedBackupService` | `household.sbmdb` | `SqliteStorageTests`, `EncryptedBackupServiceTests` | Isolated restore | Launch to Dashboard | Complete | No database encryption; no signing cert |
| Undo, archive, audit | Session undo stack; member archive; `audit_log` | Workspace, Household, Settings | `BudgetSession.Undo`, `ReadAuditTrail` | `audit_log` v19 | `BudgetSessionUndoTests` | Counts only in logs | Undo a test edit | Complete | Some destructive ops remain confirm-only |
| Full UI and Help | All nav items wired; Help glossary | All listed screens | ViewModels | Session document | App.Tests ViewModels, `FinanceGlossaryTests` | Navigation compile | Keyboard pass | Complete | Physical dual-monitor live check |
| Capture | Ctrl+S full page | All pages | `WpfFullPageCaptureService` | PNG (unencrypted) | Capture tests | Privacy warning | Capture long pages | Complete | PNG is plaintext |
| Installer | Self-contained publish + Inno/PowerShell | n/a | `packaging/*` | No household data in payload | Publish file presence | Isolated install | Start-menu on this PC | Complete | Signing cert unavailable; second PC unavailable |

## Schema

Latest version **19**. Forward-only migrations. Failed saves do not replace the previous document. A failed restore leaves the previous live file in place.

## Packaging

- Release executable: `src\SecureBudgetManager.App\bin\Release\net8.0-windows\publish\win-x64\SecureBudgetManager.exe`
- Installer or portable zip: `dist\`
- Data directory: `%LocalAppData%\SecureBudgetManager` unless `SECURE_BUDGET_MANAGER_DATA_ROOT` is set
