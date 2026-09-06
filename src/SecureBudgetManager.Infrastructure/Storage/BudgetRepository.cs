using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Benefits;
using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Goals;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.International;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Products;
using SecureBudgetManager.Core.Planning;
using SecureBudgetManager.Core.Savings;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Infrastructure.Storage;

/// <summary>
/// Reads and writes the household dataset in the local SQLite database.
///
/// Saves replace the whole document inside one transaction. For a dataset of this size that is
/// both fast enough and far safer than incremental updates, because there is no way to end up
/// with an expense whose split rule was only half written.
/// </summary>
public sealed class BudgetRepository : IBudgetRepository
{
    private readonly IBudgetDatabase _database;
    private readonly ILogger<BudgetRepository> _logger;

    public BudgetRepository(IBudgetDatabase database, ILogger<BudgetRepository> logger)
    {
        _database = database;
        _logger = logger;
    }

    public bool HasData() => _database.Read(connection =>
        Scalar(connection, "SELECT COUNT(*) FROM household_member;") > 0);

    public BudgetDocument Load() => _database.Read(connection =>
    {
        var (name, preferences) = LoadHousehold(connection);

        return new BudgetDocument
        {
            HouseholdName = name,
            Preferences = preferences,
            Members = LoadMembers(connection),
            Accounts = LoadAccounts(connection),
            IncomeSources = LoadIncomeSources(connection),
            Payslips = LoadPayslips(connection),
            PayrollProfiles = LoadPayrollProfiles(connection),
            Expenses = LoadExpenses(connection),
            Transactions = LoadTransactions(connection),
            Benefits = LoadBenefits(connection),
            Debts = LoadDebts(connection),
            Funds = LoadFunds(connection),
            Goals = LoadGoals(connection),
            Scenarios = LoadScenarios(connection),
            Rules = LoadAllocationRules(connection),
            Hierarchy = LoadHierarchy(connection),
            GroceryPlans = LoadGroceryPlans(connection),
            CostGuidance = LoadCostGuidance(connection),
            Reserves = LoadReserves(connection),
            Transfers = LoadTransfers(connection),
            ReimbursementOffsets = LoadReimbursementOffsets(connection),
            Locality = LoadLocality(connection),
            Products = LoadProducts(connection),
            PriceObservations = LoadPriceObservations(connection),
            SalesTaxRules = LoadSalesTaxRules(connection),
            ExchangeRates = LoadExchangeRates(connection),
            InternationalTransfers = LoadInternationalTransfers(connection),
            SupportCommitments = LoadSupportCommitments(connection),
            ForeignAccounts = LoadForeignAccounts(connection),
            SupportingDocuments = LoadSupportingDocuments(connection),
            ImportedGuidancePackVersion = LoadGuidancePackVersion(connection),
            OutstandingQuestions = LoadOutstandingQuestions(connection)
        };
    });

    public void Save(BudgetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Validating before the transaction opens means an invalid document never touches disk.
        document.Validate();

        _database.WriteTransaction((connection, transaction) =>
        {
            // Child rows go first: foreign keys are on, so parents cannot be removed underneath them.
            foreach (var table in DeleteOrder)
            {
                Execute(connection, transaction, $"DELETE FROM {table};");
            }

            SaveHousehold(connection, transaction, document);
            SaveMembers(connection, transaction, document.Members);
            SaveAccounts(connection, transaction, document.Accounts);
            SaveIncomeSources(connection, transaction, document.IncomeSources);
            SavePayslips(connection, transaction, document.Payslips);
            SavePayrollProfiles(connection, transaction, document.PayrollProfiles);
            SaveExpenses(connection, transaction, document.Expenses);
            SaveTransactions(connection, transaction, document.Transactions);
            SaveBenefits(connection, transaction, document.Benefits);
            SaveDebts(connection, transaction, document.Debts);
            SaveFunds(connection, transaction, document.Funds);
            SaveGoals(connection, transaction, document.Goals);
            SaveScenarios(connection, transaction, document.Scenarios);
            SaveAllocationRules(connection, transaction, document.Rules, document.Hierarchy, document.Locality);
            SaveHierarchy(connection, transaction, document.Hierarchy);
            SaveGroceryPlans(connection, transaction, document.GroceryPlans);
            SaveCostGuidance(connection, transaction, document.CostGuidance);
            SaveReserves(connection, transaction, document.Reserves);
            SaveTransfers(connection, transaction, document.Transfers);
            SaveReimbursementOffsets(connection, transaction, document.ReimbursementOffsets);
            SaveProducts(connection, transaction, document.Products);
            SavePriceObservations(connection, transaction, document.PriceObservations);
            SaveSalesTaxRules(connection, transaction, document.SalesTaxRules);
            SaveExchangeRates(connection, transaction, document.ExchangeRates);
            SaveInternationalTransfers(connection, transaction, document.InternationalTransfers);
            SaveSupportCommitments(connection, transaction, document.SupportCommitments);
            SaveForeignAccounts(connection, transaction, document.ForeignAccounts);
            SaveSupportingDocuments(connection, transaction, document.SupportingDocuments);
            SaveGuidancePackVersion(connection, transaction, document.ImportedGuidancePackVersion);
            SaveOutstandingQuestions(connection, transaction, document.OutstandingQuestions);
        });

        // The audit trail records that a change happened, never what the figures were.
        _database.RecordAuditEvent(
            "household-data-saved",
            $"members={document.Members.Count}; income={document.IncomeSources.Count}; " +
            $"expenses={document.Expenses.Count}; scenarios={document.Scenarios.Count}");

        _logger.LogInformation(
            "Household data saved: {Members} member(s), {Expenses} expense(s), {Scenarios} scenario(s).",
            document.Members.Count,
            document.Expenses.Count,
            document.Scenarios.Count);
    }

    private static readonly string[] DeleteOrder =
    [
        "outstanding_question",
        "supporting_document",
        "foreign_account",
        "support_commitment",
        "international_transfer",
        "exchange_rate_quote",
        "sales_tax_rule",
        "price_observation",
        "product_substitute",
        "product",
        "guidance_pack_state",
        "allocation_funding_step",
        "reimbursement_offset",
        "personal_transfer",
        "obligation_reserve",
        "cost_guidance",
        "grocery_category",
        "grocery_plan_assistance_category",
        "grocery_plan",
        "needs_category_rule",
        "allocation_reduction_step",
        "allocation_surplus_rule",
        "allocation_member_percent",
        "allocation_rules",
        "scenario_line",
        "scenario",
        "goal",
        "savings_fund",
        "debt_account",
        "payroll_deduction",
        "payroll_profile",
        "benefit_beneficiary",
        "benefit_plan",
        "expense_transaction",
        "expense_split_participant",
        "expense_item",
        "payslip",
        "income_source",
        "bank_account",
        "household_member",
        "household"
    ];

    #region Household

    private static (string Name, HouseholdPreferences Preferences) LoadHousehold(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM household WHERE id = 1;";
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return ("Our household", new HouseholdPreferences());
        }

        return (
            reader.GetString(reader.GetOrdinal("name")),
            new HouseholdPreferences
            {
                CurrencySymbol = reader.GetString(reader.GetOrdinal("currency_symbol")),
                DateFormat = reader.GetString(reader.GetOrdinal("date_format")),
                MinimumBalanceReserve = SqliteValues.GetMoney(reader, "minimum_balance_reserve"),
                MinimumBreathingRoom = SqliteValues.GetMoney(reader, "minimum_breathing_room"),
                EmergencyFundTargetMonths = SqliteValues.GetDecimal(reader, "emergency_fund_target_months"),
                ForecastHorizonMonths = SqliteValues.GetInt(reader, "forecast_horizon_months")
            });
    }

    private static void SaveHousehold(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BudgetDocument document)
    {
        using var command = Command(connection, transaction, """
            INSERT INTO household (
                id, name, currency_symbol, date_format,
                minimum_balance_reserve, minimum_breathing_room, emergency_fund_target_months,
                forecast_horizon_months)
            VALUES (1, $name, $symbol, $format, $reserve, $room, $months, $horizon);
            """);

        command.Parameters.AddWithValue("$name", document.HouseholdName);
        command.Parameters.AddWithValue("$symbol", document.Preferences.CurrencySymbol);
        command.Parameters.AddWithValue("$format", document.Preferences.DateFormat);
        command.Parameters.AddWithValue("$reserve", SqliteValues.ToText(document.Preferences.MinimumBalanceReserve));
        command.Parameters.AddWithValue("$room", SqliteValues.ToText(document.Preferences.MinimumBreathingRoom));
        command.Parameters.AddWithValue("$months", SqliteValues.ToText(document.Preferences.EmergencyFundTargetMonths));
        command.Parameters.AddWithValue("$horizon", document.Preferences.ForecastHorizonMonths);
        command.ExecuteNonQuery();
    }

    private static List<HouseholdMember> LoadMembers(SqliteConnection connection)
    {
        var members = new List<HouseholdMember>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM household_member ORDER BY is_dependant, name;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            members.Add(new HouseholdMember
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                Name = reader.GetString(reader.GetOrdinal("name")),
                IsDependant = SqliteValues.GetBool(reader, "is_dependant"),
                DateOfBirth = SqliteValues.GetNullableDate(reader, "date_of_birth"),
                PersonalAllowance = SqliteValues.GetMoney(reader, "personal_allowance"),
                PersonalAllowanceFrequency = SqliteValues.GetEnum<Frequency>(reader, "personal_allowance_frequency"),
                IsDiscretionaryEligible = SqliteValues.GetBool(reader, "is_discretionary_eligible"),
                Notes = SqliteValues.GetNullableString(reader, "notes"),
                IsArchived = SqliteValues.GetBool(reader, "is_archived"),
                EffectiveFrom = SqliteValues.GetNullableDate(reader, "effective_from"),
                EffectiveTo = SqliteValues.GetNullableDate(reader, "effective_to"),
                ParticipatesInSharedCosts = SqliteValues.GetBool(reader, "participates_in_shared_costs")
            });
        }

        return members;
    }

    private static void SaveMembers(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<HouseholdMember> members)
    {
        foreach (var member in members)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO household_member (
                    id, name, is_dependant, date_of_birth,
                    personal_allowance, personal_allowance_frequency, is_discretionary_eligible,
                    notes, is_archived, effective_from, effective_to, participates_in_shared_costs)
                VALUES ($id, $name, $dependant, $birth, $allowance, $frequency, $eligible,
                    $notes, $archived, $from, $to, $shared);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(member.Id));
            command.Parameters.AddWithValue("$name", member.Name);
            command.Parameters.AddWithValue("$dependant", member.IsDependant ? 1 : 0);
            command.Parameters.AddWithValue("$birth", SqliteValues.ToNullable(member.DateOfBirth));
            command.Parameters.AddWithValue("$allowance", SqliteValues.ToText(member.PersonalAllowance));
            command.Parameters.AddWithValue("$frequency", (int)member.PersonalAllowanceFrequency);
            command.Parameters.AddWithValue("$eligible", member.IsDiscretionaryEligible ? 1 : 0);
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(member.Notes));
            command.Parameters.AddWithValue("$archived", member.IsArchived ? 1 : 0);
            command.Parameters.AddWithValue("$from", SqliteValues.ToNullable(member.EffectiveFrom));
            command.Parameters.AddWithValue("$to", SqliteValues.ToNullable(member.EffectiveTo));
            command.Parameters.AddWithValue("$shared", member.ParticipatesInSharedCosts ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private static List<BankAccount> LoadAccounts(SqliteConnection connection)
    {
        var accounts = new List<BankAccount>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM bank_account ORDER BY is_primary DESC, name;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            accounts.Add(new BankAccount
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                Name = reader.GetString(reader.GetOrdinal("name")),
                CurrentBalance = SqliteValues.GetMoney(reader, "current_balance"),
                IsPrimary = SqliteValues.GetBool(reader, "is_primary"),
                OwnerMemberId = SqliteValues.GetNullableGuid(reader, "owner_member_id"),
                UpdatedOn = SqliteValues.GetDate(reader, "updated_on")
            });
        }

        return accounts;
    }

    private static void SaveAccounts(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<BankAccount> accounts)
    {
        foreach (var account in accounts)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO bank_account (id, name, current_balance, is_primary, owner_member_id, updated_on)
                VALUES ($id, $name, $balance, $primary, $owner, $updated);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(account.Id));
            command.Parameters.AddWithValue("$name", account.Name);
            command.Parameters.AddWithValue("$balance", SqliteValues.ToText(account.CurrentBalance));
            command.Parameters.AddWithValue("$primary", account.IsPrimary ? 1 : 0);
            command.Parameters.AddWithValue("$owner", SqliteValues.ToNullable(account.OwnerMemberId));
            command.Parameters.AddWithValue("$updated", SqliteValues.ToText(account.UpdatedOn));
            command.ExecuteNonQuery();
        }
    }

    #endregion

    #region Income

    private static List<IncomeSource> LoadIncomeSources(SqliteConnection connection)
    {
        var sources = new List<IncomeSource>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM income_source ORDER BY name;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var id = SqliteValues.GetGuid(reader, "id");
            var name = reader.GetString(reader.GetOrdinal("name"));
            var memberId = SqliteValues.GetGuid(reader, "member_id");
            var frequency = SqliteValues.GetEnum<Frequency>(reader, "pay_frequency");
            var anchor = SqliteValues.GetDate(reader, "anchor_pay_date");
            var isTaxable = SqliteValues.GetBool(reader, "is_taxable");
            var isActive = SqliteValues.GetBool(reader, "is_active");
            var kind = reader.GetString(reader.GetOrdinal("kind"));

            IncomeSource source = kind switch
            {
                "hourly" => new HourlyIncome
                {
                    Id = id,
                    Name = name,
                    MemberId = memberId,
                    PayFrequency = frequency,
                    AnchorPayDate = anchor,
                    IsTaxable = isTaxable,
                    IsActive = isActive,
                    HourlyRate = SqliteValues.GetMoneyOrZero(reader, "hourly_rate"),
                    WeeklyHours = new VariableHours(
                        SqliteValues.GetDecimalOrZero(reader, "hours_conservative"),
                        SqliteValues.GetDecimalOrZero(reader, "hours_normal"),
                        SqliteValues.GetDecimalOrZero(reader, "hours_optimistic")),
                    WeeklyOvertimeHours = new VariableHours(
                        SqliteValues.GetDecimalOrZero(reader, "overtime_conservative"),
                        SqliteValues.GetDecimalOrZero(reader, "overtime_normal"),
                        SqliteValues.GetDecimalOrZero(reader, "overtime_optimistic")),
                    OvertimeMultiplier = SqliteValues.GetNullableDecimal(reader, "overtime_multiplier") ?? 1.5m,
                    PaidLeaveHoursPerPeriod = SqliteValues.GetDecimalOrZero(reader, "paid_leave_hours"),
                    UnpaidLeaveHoursPerPeriod = SqliteValues.GetDecimalOrZero(reader, "unpaid_leave_hours")
                },

                "salary" => new SalaryIncome
                {
                    Id = id,
                    Name = name,
                    MemberId = memberId,
                    PayFrequency = frequency,
                    AnchorPayDate = anchor,
                    IsTaxable = isTaxable,
                    IsActive = isActive,
                    AnnualSalary = SqliteValues.GetMoneyOrZero(reader, "annual_salary")
                },

                "variable" => new VariableIncome
                {
                    Id = id,
                    Name = name,
                    MemberId = memberId,
                    PayFrequency = frequency,
                    AnchorPayDate = anchor,
                    IsTaxable = isTaxable,
                    IsActive = isActive,
                    AmountPerPeriod = new VariableHours(
                        SqliteValues.GetDecimalOrZero(reader, "amount_conservative"),
                        SqliteValues.GetDecimalOrZero(reader, "amount_normal"),
                        SqliteValues.GetDecimalOrZero(reader, "amount_optimistic"))
                },

                "mileage" => new MileageReimbursement
                {
                    Id = id,
                    Name = name,
                    MemberId = memberId,
                    PayFrequency = frequency,
                    AnchorPayDate = anchor,
                    IsTaxable = false,
                    IsActive = isActive,
                    MilesPerPeriod = SqliteValues.GetDecimalOrZero(reader, "miles_per_period"),
                    RatePerMile = SqliteValues.GetMoneyOrZero(reader, "rate_per_mile")
                },

                _ => throw new InvalidOperationException($"Unknown income kind \"{kind}\" in the database.")
            };

            sources.Add(source with
            {
                PayScheduleConfirmed = SqliteValues.GetBool(reader, "pay_schedule_confirmed"),
                Notes = SqliteValues.GetNullableString(reader, "notes"),
                StartsOn = SqliteValues.GetNullableDate(reader, "starts_on"),
                EndsOn = SqliteValues.GetNullableDate(reader, "ends_on"),
                Role = kind == "mileage"
                    ? IncomeRole.Reimbursement
                    : SqliteValues.GetEnum<IncomeRole>(reader, "income_role")
            });
        }

        return sources;
    }

    private static void SaveIncomeSources(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<IncomeSource> sources)
    {
        foreach (var source in sources)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO income_source (
                    id, kind, name, member_id, pay_frequency, anchor_pay_date, is_taxable, is_active,
                    pay_schedule_confirmed, notes, starts_on, ends_on, income_role,
                    hourly_rate, hours_conservative, hours_normal, hours_optimistic,
                    overtime_conservative, overtime_normal, overtime_optimistic, overtime_multiplier,
                    paid_leave_hours, unpaid_leave_hours, annual_salary,
                    amount_conservative, amount_normal, amount_optimistic,
                    miles_per_period, rate_per_mile)
                VALUES (
                    $id, $kind, $name, $member, $frequency, $anchor, $taxable, $active,
                    $scheduleConfirmed, $notes, $startsOn, $endsOn, $role,
                    $rate, $hoursLow, $hoursMid, $hoursHigh,
                    $otLow, $otMid, $otHigh, $otMultiplier,
                    $paidLeave, $unpaidLeave, $salary,
                    $amountLow, $amountMid, $amountHigh,
                    $miles, $ratePerMile);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(source.Id));
            command.Parameters.AddWithValue("$kind", KindOf(source));
            command.Parameters.AddWithValue("$name", source.Name);
            command.Parameters.AddWithValue("$member", SqliteValues.ToText(source.MemberId));
            command.Parameters.AddWithValue("$frequency", (int)source.PayFrequency);
            command.Parameters.AddWithValue("$anchor", SqliteValues.ToText(source.AnchorPayDate));
            command.Parameters.AddWithValue("$taxable", source.IsTaxable ? 1 : 0);
            command.Parameters.AddWithValue("$active", source.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$scheduleConfirmed", source.PayScheduleConfirmed ? 1 : 0);
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(source.Notes));
            command.Parameters.AddWithValue("$startsOn", SqliteValues.ToNullable(source.StartsOn));
            command.Parameters.AddWithValue("$endsOn", SqliteValues.ToNullable(source.EndsOn));
            command.Parameters.AddWithValue("$role", (int)source.Role);

            var hourly = source as HourlyIncome;
            var variable = source as VariableIncome;
            var mileage = source as MileageReimbursement;

            command.Parameters.AddWithValue("$rate", SqliteValues.ToNullable(hourly?.HourlyRate));
            command.Parameters.AddWithValue("$hoursLow", SqliteValues.ToNullable(hourly?.WeeklyHours.Conservative));
            command.Parameters.AddWithValue("$hoursMid", SqliteValues.ToNullable(hourly?.WeeklyHours.Normal));
            command.Parameters.AddWithValue("$hoursHigh", SqliteValues.ToNullable(hourly?.WeeklyHours.Optimistic));
            command.Parameters.AddWithValue("$otLow", SqliteValues.ToNullable(hourly?.WeeklyOvertimeHours.Conservative));
            command.Parameters.AddWithValue("$otMid", SqliteValues.ToNullable(hourly?.WeeklyOvertimeHours.Normal));
            command.Parameters.AddWithValue("$otHigh", SqliteValues.ToNullable(hourly?.WeeklyOvertimeHours.Optimistic));
            command.Parameters.AddWithValue("$otMultiplier", SqliteValues.ToNullable(hourly?.OvertimeMultiplier));
            command.Parameters.AddWithValue("$paidLeave", SqliteValues.ToNullable(hourly?.PaidLeaveHoursPerPeriod));
            command.Parameters.AddWithValue("$unpaidLeave", SqliteValues.ToNullable(hourly?.UnpaidLeaveHoursPerPeriod));
            command.Parameters.AddWithValue("$salary", SqliteValues.ToNullable((source as SalaryIncome)?.AnnualSalary));
            command.Parameters.AddWithValue("$amountLow", SqliteValues.ToNullable(variable?.AmountPerPeriod.Conservative));
            command.Parameters.AddWithValue("$amountMid", SqliteValues.ToNullable(variable?.AmountPerPeriod.Normal));
            command.Parameters.AddWithValue("$amountHigh", SqliteValues.ToNullable(variable?.AmountPerPeriod.Optimistic));
            command.Parameters.AddWithValue("$miles", SqliteValues.ToNullable(mileage?.MilesPerPeriod));
            command.Parameters.AddWithValue("$ratePerMile", SqliteValues.ToNullable(mileage?.RatePerMile));
            command.ExecuteNonQuery();
        }
    }

    private static string KindOf(IncomeSource source) => source switch
    {
        HourlyIncome => "hourly",
        SalaryIncome => "salary",
        VariableIncome => "variable",
        MileageReimbursement => "mileage",
        _ => throw new InvalidOperationException($"Cannot store income source of type {source.GetType().Name}.")
    };

    private static List<Payslip> LoadPayslips(SqliteConnection connection)
    {
        var payslips = new List<Payslip>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM payslip ORDER BY pay_date DESC;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            payslips.Add(new Payslip
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                IncomeSourceId = SqliteValues.GetGuid(reader, "income_source_id"),
                PayDate = SqliteValues.GetDate(reader, "pay_date"),
                GrossPay = SqliteValues.GetMoney(reader, "gross_pay"),
                PreTaxDeductions = SqliteValues.GetMoney(reader, "pre_tax_deductions"),
                FederalWithholding = SqliteValues.GetMoney(reader, "federal_withholding"),
                StateWithholding = SqliteValues.GetMoney(reader, "state_withholding"),
                SocialSecurity = SqliteValues.GetMoney(reader, "social_security"),
                Medicare = SqliteValues.GetMoney(reader, "medicare"),
                PostTaxDeductions = SqliteValues.GetMoney(reader, "post_tax_deductions"),
                NonTaxableReimbursements = SqliteValues.GetMoney(reader, "non_taxable_reimbursements"),
                HoursWorked = SqliteValues.GetDecimal(reader, "hours_worked"),
                OvertimeHoursWorked = SqliteValues.GetDecimal(reader, "overtime_hours_worked"),
                TaxYear = SqliteValues.GetNullableInt(reader, "tax_year"),
                RuleSetVersion = SqliteValues.GetNullableString(reader, "rule_set_version")
            });
        }

        return payslips;
    }

    private static void SavePayslips(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<Payslip> payslips)
    {
        foreach (var payslip in payslips)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO payslip (
                    id, income_source_id, pay_date, gross_pay, pre_tax_deductions,
                    federal_withholding, state_withholding, social_security, medicare,
                    post_tax_deductions, non_taxable_reimbursements, hours_worked, overtime_hours_worked,
                    tax_year, rule_set_version)
                VALUES (
                    $id, $source, $date, $gross, $preTax, $federal, $state, $socialSecurity,
                    $medicare, $postTax, $reimbursements, $hours, $overtime, $taxYear, $ruleVersion);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(payslip.Id));
            command.Parameters.AddWithValue("$source", SqliteValues.ToText(payslip.IncomeSourceId));
            command.Parameters.AddWithValue("$date", SqliteValues.ToText(payslip.PayDate));
            command.Parameters.AddWithValue("$gross", SqliteValues.ToText(payslip.GrossPay));
            command.Parameters.AddWithValue("$preTax", SqliteValues.ToText(payslip.PreTaxDeductions));
            command.Parameters.AddWithValue("$federal", SqliteValues.ToText(payslip.FederalWithholding));
            command.Parameters.AddWithValue("$state", SqliteValues.ToText(payslip.StateWithholding));
            command.Parameters.AddWithValue("$socialSecurity", SqliteValues.ToText(payslip.SocialSecurity));
            command.Parameters.AddWithValue("$medicare", SqliteValues.ToText(payslip.Medicare));
            command.Parameters.AddWithValue("$postTax", SqliteValues.ToText(payslip.PostTaxDeductions));
            command.Parameters.AddWithValue("$reimbursements", SqliteValues.ToText(payslip.NonTaxableReimbursements));
            command.Parameters.AddWithValue("$hours", SqliteValues.ToText(payslip.HoursWorked));
            command.Parameters.AddWithValue("$overtime", SqliteValues.ToText(payslip.OvertimeHoursWorked));
            command.Parameters.AddWithValue("$taxYear", payslip.TaxYear.HasValue ? payslip.TaxYear.Value : DBNull.Value);
            command.Parameters.AddWithValue("$ruleVersion", SqliteValues.ToNullable(payslip.RuleSetVersion));
            command.ExecuteNonQuery();
        }
    }

    private static List<PayrollProfile> LoadPayrollProfiles(SqliteConnection connection)
    {
        var deductionsByMember = LoadPayrollDeductions(connection);
        var profiles = new List<PayrollProfile>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM payroll_profile;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var memberId = SqliteValues.GetGuid(reader, "member_id");

            profiles.Add(new PayrollProfile
            {
                MemberId = memberId,
                TaxYear = SqliteValues.GetInt(reader, "tax_year"),
                W4 = new W4Settings
                {
                    FilingStatus = SqliteValues.GetEnum<FilingStatus>(reader, "filing_status"),
                    QualifyingChildren = SqliteValues.GetInt(reader, "qualifying_children"),
                    OtherDependants = SqliteValues.GetInt(reader, "other_dependants"),
                    OtherAnnualIncome = SqliteValues.GetMoney(reader, "other_annual_income"),
                    AnnualDeductions = SqliteValues.GetMoney(reader, "annual_deductions"),
                    ExtraWithholdingPerPeriod = SqliteValues.GetMoney(reader, "extra_withholding_per_period"),
                    MultipleJobsChecked = SqliteValues.GetBool(reader, "multiple_jobs_checked"),
                    IsNonResidentAlien = SqliteValues.GetBool(reader, "is_non_resident_alien")
                },
                Retirement = new RetirementPlan
                {
                    EmployeeContributionPercent = SqliteValues.GetDecimal(reader, "retirement_employee_percent"),
                    EmployerMatchPercent = SqliteValues.GetDecimal(reader, "retirement_employer_percent"),
                    EmployerMatchLimitPercent = SqliteValues.GetDecimal(reader, "retirement_employer_limit_percent"),
                    VestingYears = SqliteValues.GetInt(reader, "retirement_vesting_years"),
                    IsRoth = SqliteValues.GetBool(reader, "retirement_is_roth")
                },
                Deductions = deductionsByMember.TryGetValue(memberId, out var deductions) ? deductions : [],
                StateFlatRate = SqliteValues.GetDecimal(reader, "state_flat_rate"),
                UsesUtahWithholding = SqliteValues.GetBool(reader, "uses_utah_withholding"),
                TaxResidency = SqliteValues.GetEnum<UsTaxResidency>(reader, "tax_residency"),
                WithholdingRuleVersion = SqliteValues.GetNullableString(reader, "withholding_rule_version"),
                YearsOfService = SqliteValues.GetDecimal(reader, "years_of_service"),
                W4IsComplete = SqliteValues.GetBool(reader, "w4_is_complete"),
                Notes = SqliteValues.GetNullableString(reader, "notes")
            });
        }

        return profiles;
    }

    private static Dictionary<Guid, List<PayrollDeduction>> LoadPayrollDeductions(SqliteConnection connection)
    {
        var result = new Dictionary<Guid, List<PayrollDeduction>>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM payroll_deduction ORDER BY member_id, position;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var memberId = SqliteValues.GetGuid(reader, "member_id");

            if (!result.TryGetValue(memberId, out var list))
            {
                list = [];
                result[memberId] = list;
            }

            list.Add(new PayrollDeduction
            {
                Name = reader.GetString(reader.GetOrdinal("name")),
                AmountPerPeriod = SqliteValues.GetMoney(reader, "amount_per_period"),
                TaxTreatment = SqliteValues.GetEnum<DeductionTaxTreatment>(reader, "tax_treatment")
            });
        }

        return result;
    }

    private static void SavePayrollProfiles(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<PayrollProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            using (var command = Command(connection, transaction, """
                INSERT INTO payroll_profile (
                    member_id, tax_year, filing_status, qualifying_children, other_dependants,
                    other_annual_income, annual_deductions, extra_withholding_per_period,
                    multiple_jobs_checked, is_non_resident_alien, state_flat_rate,
                    retirement_employee_percent, retirement_employer_percent,
                    retirement_employer_limit_percent, retirement_vesting_years,
                    retirement_is_roth, years_of_service, uses_utah_withholding, tax_residency,
                    withholding_rule_version, w4_is_complete, notes)
                VALUES (
                    $member, $year, $status, $children, $dependants,
                    $otherIncome, $deductions, $extra,
                    $multipleJobs, $nra, $stateRate,
                    $employeePercent, $employerPercent, $employerLimit, $vesting, $roth, $service,
                    $utah, $residency, $ruleVersion, $w4Complete, $notes);
                """))
            {
                command.Parameters.AddWithValue("$member", SqliteValues.ToText(profile.MemberId));
                command.Parameters.AddWithValue("$year", profile.TaxYear);
                command.Parameters.AddWithValue("$status", (int)profile.W4.FilingStatus);
                command.Parameters.AddWithValue("$children", profile.W4.QualifyingChildren);
                command.Parameters.AddWithValue("$dependants", profile.W4.OtherDependants);
                command.Parameters.AddWithValue("$otherIncome", SqliteValues.ToText(profile.W4.OtherAnnualIncome));
                command.Parameters.AddWithValue("$deductions", SqliteValues.ToText(profile.W4.AnnualDeductions));
                command.Parameters.AddWithValue("$extra", SqliteValues.ToText(profile.W4.ExtraWithholdingPerPeriod));
                command.Parameters.AddWithValue("$multipleJobs", profile.W4.MultipleJobsChecked ? 1 : 0);
                command.Parameters.AddWithValue("$nra", profile.W4.IsNonResidentAlien ? 1 : 0);
                command.Parameters.AddWithValue("$stateRate", SqliteValues.ToText(profile.StateFlatRate));
                command.Parameters.AddWithValue(
                    "$employeePercent",
                    SqliteValues.ToText(profile.Retirement.EmployeeContributionPercent));
                command.Parameters.AddWithValue(
                    "$employerPercent",
                    SqliteValues.ToText(profile.Retirement.EmployerMatchPercent));
                command.Parameters.AddWithValue(
                    "$employerLimit",
                    SqliteValues.ToText(profile.Retirement.EmployerMatchLimitPercent));
                command.Parameters.AddWithValue("$vesting", profile.Retirement.VestingYears);
                command.Parameters.AddWithValue("$roth", profile.Retirement.IsRoth ? 1 : 0);
                command.Parameters.AddWithValue("$service", SqliteValues.ToText(profile.YearsOfService));
                command.Parameters.AddWithValue("$utah", profile.UsesUtahWithholding ? 1 : 0);
                command.Parameters.AddWithValue("$residency", (int)profile.TaxResidency);
                command.Parameters.AddWithValue("$ruleVersion", SqliteValues.ToNullable(profile.WithholdingRuleVersion));
                command.Parameters.AddWithValue("$w4Complete", profile.W4IsComplete ? 1 : 0);
                command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(profile.Notes));
                command.ExecuteNonQuery();
            }

            for (var position = 0; position < profile.Deductions.Count; position++)
            {
                var deduction = profile.Deductions[position];

                using var command = Command(connection, transaction, """
                    INSERT INTO payroll_deduction (id, member_id, name, amount_per_period, tax_treatment, position)
                    VALUES ($id, $member, $name, $amount, $treatment, $position);
                    """);

                command.Parameters.AddWithValue("$id", SqliteValues.ToText(Guid.NewGuid()));
                command.Parameters.AddWithValue("$member", SqliteValues.ToText(profile.MemberId));
                command.Parameters.AddWithValue("$name", deduction.Name);
                command.Parameters.AddWithValue("$amount", SqliteValues.ToText(deduction.AmountPerPeriod));
                command.Parameters.AddWithValue("$treatment", (int)deduction.TaxTreatment);
                command.Parameters.AddWithValue("$position", position);
                command.ExecuteNonQuery();
            }
        }
    }

    #endregion

    #region Expenses

    private static List<ExpenseItem> LoadExpenses(SqliteConnection connection)
    {
        var splits = LoadSplitParticipants(connection);
        var expenses = new List<ExpenseItem>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM expense_item ORDER BY name;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var id = SqliteValues.GetGuid(reader, "id");
            var splitMethod = SqliteValues.GetNullableEnum<SplitMethod>(reader, "split_method");

            SplitRule? split = null;
            if (splitMethod is { } method && splits.TryGetValue(id, out var rows))
            {
                split = new SplitRule
                {
                    Method = method,
                    Participants = rows.Select(row => row.MemberId).ToList(),
                    Percentages = method == SplitMethod.Percentage
                        ? rows.Select(row => row.Percentage ?? 0m).ToList()
                        : [],
                    FixedAmounts = method == SplitMethod.FixedAmount
                        ? rows.Select(row => row.FixedAmount ?? Money.Zero).ToList()
                        : []
                };
            }

            expenses.Add(new ExpenseItem
            {
                Id = id,
                Name = reader.GetString(reader.GetOrdinal("name")),
                Category = new ExpenseCategory(
                    reader.GetString(reader.GetOrdinal("category_name")),
                    SqliteValues.GetBool(reader, "category_is_built_in")),
                ExpectedAmount = SqliteValues.GetMoney(reader, "expected_amount"),
                Frequency = SqliteValues.GetEnum<Frequency>(reader, "frequency"),
                AnchorDueDate = SqliteValues.GetDate(reader, "anchor_due_date"),
                Necessity = SqliteValues.GetEnum<ExpenseNecessity>(reader, "necessity"),
                Variability = SqliteValues.GetEnum<ExpenseVariability>(reader, "variability"),
                Ownership = SqliteValues.GetEnum<Ownership>(reader, "ownership"),
                Assignment = SqliteValues.GetEnumOrDefault(reader, "assignment", BillAssignment.Unassigned),
                Split = split,
                AutopayAnchorDate = SqliteValues.GetNullableDate(reader, "autopay_anchor_date"),
                IsPaused = SqliteValues.GetBool(reader, "is_paused"),
                DueDateUnknown = SqliteValues.GetBool(reader, "due_date_unknown"),
                ScheduleConfirmed = SqliteValues.GetBoolOrDefault(reader, "schedule_confirmed", true),
                IsArchived = SqliteValues.GetBool(reader, "is_archived"),
                DueDateAdjustment = SqliteValues.GetEnum<DueDateAdjustment>(reader, "due_date_adjustment"),
                EndsOn = SqliteValues.GetNullableDate(reader, "ends_on"),
                Notes = SqliteValues.GetNullableString(reader, "notes"),
                Coverage = SqliteValues.GetEnumOrDefault(reader, "coverage", HouseholdCostCoverage.HouseholdPays),
                CoverageConfirmed = SqliteValues.GetBoolOrDefault(reader, "coverage_confirmed", false),
                CoveredByExplanation = SqliteValues.GetNullableString(reader, "covered_by_explanation"),
                CoveredByExpenseId = SqliteValues.GetNullableGuid(reader, "covered_by_expense_id"),
                OriginatedAsSuggestion = SqliteValues.GetBoolOrDefault(reader, "originated_as_suggestion", false),
                SuggestionSource = SqliteValues.GetNullableString(reader, "suggestion_source"),
                SuggestionEffectiveDate = SqliteValues.GetNullableDate(reader, "suggestion_effective_date"),
                AmountKind = SqliteValues.GetEnumOrDefault(reader, "amount_kind", SuggestionAmountKind.UserDefined),
                AnnualIncreasePercent = SqliteValues.GetDecimal(reader, "annual_increase_percent")
            });
        }

        return expenses;
    }

    private sealed record SplitRow(Guid MemberId, decimal? Percentage, Money? FixedAmount);

    private static Dictionary<Guid, List<SplitRow>> LoadSplitParticipants(SqliteConnection connection)
    {
        var result = new Dictionary<Guid, List<SplitRow>>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM expense_split_participant ORDER BY expense_item_id, position;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var expenseId = SqliteValues.GetGuid(reader, "expense_item_id");

            if (!result.TryGetValue(expenseId, out var list))
            {
                list = [];
                result[expenseId] = list;
            }

            list.Add(new SplitRow(
                SqliteValues.GetGuid(reader, "member_id"),
                SqliteValues.GetNullableDecimal(reader, "percentage"),
                SqliteValues.GetNullableMoney(reader, "fixed_amount")));
        }

        return result;
    }

    private static void SaveExpenses(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<ExpenseItem> expenses)
    {
        foreach (var expense in expenses)
        {
            using (var command = Command(connection, transaction, """
                INSERT INTO expense_item (
                    id, name, category_name, category_is_built_in, expected_amount, frequency,
                    anchor_due_date, necessity, variability, ownership, assignment, split_method,
                    autopay_anchor_date, is_paused, due_date_unknown, schedule_confirmed, is_archived, due_date_adjustment,
                    ends_on, notes, coverage, coverage_confirmed, covered_by_explanation, covered_by_expense_id,
                    originated_as_suggestion, suggestion_source, suggestion_effective_date, amount_kind,
                    annual_increase_percent)
                VALUES (
                    $id, $name, $category, $builtIn, $amount, $frequency,
                    $anchor, $necessity, $variability, $ownership, $assignment, $splitMethod,
                    $autopay, $paused, $dueUnknown, $scheduleConfirmed, $archived, $adjustment,
                    $ends, $notes, $coverage, $coverageConfirmed, $coveredBy, $coveredByExpense,
                    $suggested, $suggestionSource, $suggestionDate, $amountKind,
                    $increase);
                """))
            {
                command.Parameters.AddWithValue("$id", SqliteValues.ToText(expense.Id));
                command.Parameters.AddWithValue("$name", expense.Name);
                command.Parameters.AddWithValue("$category", expense.Category.Name);
                command.Parameters.AddWithValue("$builtIn", expense.Category.IsBuiltIn ? 1 : 0);
                command.Parameters.AddWithValue("$amount", SqliteValues.ToText(expense.ExpectedAmount));
                command.Parameters.AddWithValue("$frequency", (int)expense.Frequency);
                command.Parameters.AddWithValue("$anchor", SqliteValues.ToText(expense.AnchorDueDate));
                command.Parameters.AddWithValue("$necessity", (int)expense.Necessity);
                command.Parameters.AddWithValue("$variability", (int)expense.Variability);
                command.Parameters.AddWithValue("$ownership", (int)expense.Ownership);
                command.Parameters.AddWithValue("$assignment", (int)expense.Assignment);
                command.Parameters.AddWithValue(
                    "$splitMethod",
                    expense.Split is { } rule ? (int)rule.Method : DBNull.Value);
                command.Parameters.AddWithValue("$autopay", SqliteValues.ToNullable(expense.AutopayAnchorDate));
                command.Parameters.AddWithValue("$paused", expense.IsPaused ? 1 : 0);
                command.Parameters.AddWithValue("$dueUnknown", expense.DueDateUnknown ? 1 : 0);
                command.Parameters.AddWithValue("$scheduleConfirmed", expense.ScheduleConfirmed ? 1 : 0);
                command.Parameters.AddWithValue("$archived", expense.IsArchived ? 1 : 0);
                command.Parameters.AddWithValue("$adjustment", (int)expense.DueDateAdjustment);
                command.Parameters.AddWithValue("$ends", SqliteValues.ToNullable(expense.EndsOn));
                command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(expense.Notes));
                command.Parameters.AddWithValue("$coverage", (int)expense.Coverage);
                command.Parameters.AddWithValue("$coverageConfirmed", expense.CoverageConfirmed ? 1 : 0);
                command.Parameters.AddWithValue("$coveredBy", SqliteValues.ToNullable(expense.CoveredByExplanation));
                command.Parameters.AddWithValue("$coveredByExpense", SqliteValues.ToNullable(expense.CoveredByExpenseId));
                command.Parameters.AddWithValue("$suggested", expense.OriginatedAsSuggestion ? 1 : 0);
                command.Parameters.AddWithValue("$suggestionSource", SqliteValues.ToNullable(expense.SuggestionSource));
                command.Parameters.AddWithValue("$suggestionDate", SqliteValues.ToNullable(expense.SuggestionEffectiveDate));
                command.Parameters.AddWithValue("$amountKind", (int)expense.AmountKind);
                command.Parameters.AddWithValue("$increase", SqliteValues.ToText(expense.AnnualIncreasePercent));
                command.ExecuteNonQuery();
            }

            if (expense.Split is not { } split)
            {
                continue;
            }

            for (var position = 0; position < split.Participants.Count; position++)
            {
                using var command = Command(connection, transaction, """
                    INSERT INTO expense_split_participant (
                        expense_item_id, position, member_id, percentage, fixed_amount)
                    VALUES ($expense, $position, $member, $percentage, $fixed);
                    """);

                command.Parameters.AddWithValue("$expense", SqliteValues.ToText(expense.Id));
                command.Parameters.AddWithValue("$position", position);
                command.Parameters.AddWithValue("$member", SqliteValues.ToText(split.Participants[position]));
                command.Parameters.AddWithValue(
                    "$percentage",
                    position < split.Percentages.Count
                        ? SqliteValues.ToText(split.Percentages[position])
                        : DBNull.Value);
                command.Parameters.AddWithValue(
                    "$fixed",
                    position < split.FixedAmounts.Count
                        ? SqliteValues.ToText(split.FixedAmounts[position])
                        : DBNull.Value);
                command.ExecuteNonQuery();
            }
        }
    }

    private static List<ExpenseTransaction> LoadTransactions(SqliteConnection connection)
    {
        var transactions = new List<ExpenseTransaction>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM expense_transaction ORDER BY occurred_on DESC;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            transactions.Add(new ExpenseTransaction
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                ExpenseItemId = SqliteValues.GetNullableGuid(reader, "expense_item_id"),
                Date = SqliteValues.GetDate(reader, "occurred_on"),
                Description = reader.GetString(reader.GetOrdinal("description")),
                Amount = SqliteValues.GetMoney(reader, "amount"),
                Category = new ExpenseCategory(
                    reader.GetString(reader.GetOrdinal("category_name")),
                    SqliteValues.GetBool(reader, "category_is_built_in")),
                IsRefund = SqliteValues.GetBool(reader, "is_refund"),
                SplitParentId = SqliteValues.GetNullableGuid(reader, "split_parent_id"),
                IsConfirmed = SqliteValues.GetBool(reader, "is_confirmed"),
                SpentByMemberId = SqliteValues.GetNullableGuid(reader, "spent_by_member_id"),
                Notes = SqliteValues.GetNullableString(reader, "notes")
            });
        }

        return transactions;
    }

    private static void SaveTransactions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<ExpenseTransaction> transactions)
    {
        // Parents must exist before children, because split lines reference their parent row.
        foreach (var item in transactions.OrderBy(item => item.SplitParentId.HasValue))
        {
            using var command = Command(connection, transaction, """
                INSERT INTO expense_transaction (
                    id, expense_item_id, occurred_on, description, amount,
                    category_name, category_is_built_in, is_refund, split_parent_id,
                    is_confirmed, notes, import_batch_id, duplicate_key, spent_by_member_id)
                VALUES (
                    $id, $expense, $date, $description, $amount,
                    $category, $builtIn, $refund, $parent, $confirmed, $notes, NULL, $duplicateKey,
                    $spentBy);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(item.Id));
            command.Parameters.AddWithValue("$expense", SqliteValues.ToNullable(item.ExpenseItemId));
            command.Parameters.AddWithValue("$date", SqliteValues.ToText(item.Date));
            command.Parameters.AddWithValue("$description", item.Description);
            command.Parameters.AddWithValue("$amount", SqliteValues.ToText(item.Amount));
            command.Parameters.AddWithValue("$category", item.Category.Name);
            command.Parameters.AddWithValue("$builtIn", item.Category.IsBuiltIn ? 1 : 0);
            command.Parameters.AddWithValue("$refund", item.IsRefund ? 1 : 0);
            command.Parameters.AddWithValue("$parent", SqliteValues.ToNullable(item.SplitParentId));
            command.Parameters.AddWithValue("$confirmed", item.IsConfirmed ? 1 : 0);
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(item.Notes));
            command.Parameters.AddWithValue("$spentBy", SqliteValues.ToNullable(item.SpentByMemberId));

            // Split lines legitimately repeat a date, amount and description, so only whole
            // transactions take part in duplicate detection.
            command.Parameters.AddWithValue(
                "$duplicateKey",
                item.SplitParentId is null ? DuplicateKey(item) : DBNull.Value);

            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Identity used to reject an import that would add the same transaction twice.
    /// </summary>
    internal static string DuplicateKey(ExpenseTransaction item) => string.Join(
        '|',
        item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        item.Amount.Round().ToString(),
        item.Description.Trim().ToLowerInvariant(),
        item.IsRefund ? "refund" : "charge");

    #endregion

    #region Benefits

    private static List<BenefitPlan> LoadBenefits(SqliteConnection connection)
    {
        var beneficiaries = LoadBeneficiaries(connection);
        var plans = new List<BenefitPlan>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM benefit_plan ORDER BY kind, name;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var id = SqliteValues.GetGuid(reader, "id");

            plans.Add(new BenefitPlan
            {
                Id = id,
                Name = reader.GetString(reader.GetOrdinal("name")),
                Kind = SqliteValues.GetEnum<BenefitKind>(reader, "kind"),
                MemberId = SqliteValues.GetGuid(reader, "member_id"),
                Coverage = SqliteValues.GetEnum<CoverageLevel>(reader, "coverage"),
                EmployeePremiumPerPeriod = SqliteValues.GetMoney(reader, "employee_premium_per_period"),
                PremiumFrequency = SqliteValues.GetEnum<Frequency>(reader, "premium_frequency"),
                EmployerContributionPerPeriod = SqliteValues.GetMoney(reader, "employer_contribution_per_period"),
                TaxTreatment = SqliteValues.GetEnum<DeductionTaxTreatment>(reader, "tax_treatment"),
                Deductible = SqliteValues.GetMoney(reader, "deductible"),
                OutOfPocketMaximum = SqliteValues.GetMoney(reader, "out_of_pocket_maximum"),
                CoverageAmount = SqliteValues.GetMoney(reader, "coverage_amount"),
                EffectiveDate = SqliteValues.GetNullableDate(reader, "effective_date"),
                RenewalDate = SqliteValues.GetNullableDate(reader, "renewal_date"),
                IsConfirmed = SqliteValues.GetBool(reader, "is_confirmed"),
                Beneficiaries = beneficiaries.TryGetValue(id, out var names) ? names : [],
                Notes = SqliteValues.GetNullableString(reader, "notes")
            });
        }

        return plans;
    }

    private static Dictionary<Guid, List<string>> LoadBeneficiaries(SqliteConnection connection)
    {
        var result = new Dictionary<Guid, List<string>>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM benefit_beneficiary ORDER BY benefit_plan_id, position;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var planId = SqliteValues.GetGuid(reader, "benefit_plan_id");

            if (!result.TryGetValue(planId, out var list))
            {
                list = [];
                result[planId] = list;
            }

            list.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        return result;
    }

    private static void SaveBenefits(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<BenefitPlan> plans)
    {
        foreach (var plan in plans)
        {
            using (var command = Command(connection, transaction, """
                INSERT INTO benefit_plan (
                    id, name, kind, member_id, coverage, employee_premium_per_period,
                    premium_frequency, employer_contribution_per_period, tax_treatment,
                    deductible, out_of_pocket_maximum, coverage_amount,
                    effective_date, renewal_date, is_confirmed, notes)
                VALUES (
                    $id, $name, $kind, $member, $coverage, $premium,
                    $frequency, $employer, $treatment,
                    $deductible, $maximum, $amount, $effective, $renewal, $confirmed, $notes);
                """))
            {
                command.Parameters.AddWithValue("$id", SqliteValues.ToText(plan.Id));
                command.Parameters.AddWithValue("$name", plan.Name);
                command.Parameters.AddWithValue("$kind", (int)plan.Kind);
                command.Parameters.AddWithValue("$member", SqliteValues.ToText(plan.MemberId));
                command.Parameters.AddWithValue("$coverage", (int)plan.Coverage);
                command.Parameters.AddWithValue("$premium", SqliteValues.ToText(plan.EmployeePremiumPerPeriod));
                command.Parameters.AddWithValue("$frequency", (int)plan.PremiumFrequency);
                command.Parameters.AddWithValue("$employer", SqliteValues.ToText(plan.EmployerContributionPerPeriod));
                command.Parameters.AddWithValue("$treatment", (int)plan.TaxTreatment);
                command.Parameters.AddWithValue("$deductible", SqliteValues.ToText(plan.Deductible));
                command.Parameters.AddWithValue("$maximum", SqliteValues.ToText(plan.OutOfPocketMaximum));
                command.Parameters.AddWithValue("$amount", SqliteValues.ToText(plan.CoverageAmount));
                command.Parameters.AddWithValue("$effective", SqliteValues.ToNullable(plan.EffectiveDate));
                command.Parameters.AddWithValue("$renewal", SqliteValues.ToNullable(plan.RenewalDate));
                command.Parameters.AddWithValue("$confirmed", plan.IsConfirmed ? 1 : 0);
                command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(plan.Notes));
                command.ExecuteNonQuery();
            }

            for (var position = 0; position < plan.Beneficiaries.Count; position++)
            {
                using var command = Command(connection, transaction, """
                    INSERT INTO benefit_beneficiary (benefit_plan_id, position, name)
                    VALUES ($plan, $position, $name);
                    """);

                command.Parameters.AddWithValue("$plan", SqliteValues.ToText(plan.Id));
                command.Parameters.AddWithValue("$position", position);
                command.Parameters.AddWithValue("$name", plan.Beneficiaries[position]);
                command.ExecuteNonQuery();
            }
        }
    }

    #endregion

    #region Debts, funds and goals

    private static List<DebtAccount> LoadDebts(SqliteConnection connection)
    {
        var debts = new List<DebtAccount>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM debt_account ORDER BY name;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            debts.Add(new DebtAccount
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Kind = SqliteValues.GetEnum<DebtKind>(reader, "kind"),
                Balance = SqliteValues.GetMoney(reader, "balance"),
                AnnualPercentageRate = SqliteValues.GetDecimal(reader, "annual_percentage_rate"),
                MinimumPayment = SqliteValues.GetMoney(reader, "minimum_payment"),
                PromotionalRate = SqliteValues.GetNullableDecimal(reader, "promotional_rate"),
                PromotionalRateEnds = SqliteValues.GetNullableDate(reader, "promotional_rate_ends"),
                OwnerMemberId = SqliteValues.GetNullableGuid(reader, "owner_member_id"),
                DueDayOfMonth = SqliteValues.GetNullableInt(reader, "due_day_of_month")
            });
        }

        return debts;
    }

    private static void SaveDebts(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<DebtAccount> debts)
    {
        foreach (var debt in debts)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO debt_account (
                    id, name, kind, balance, annual_percentage_rate, minimum_payment,
                    promotional_rate, promotional_rate_ends, owner_member_id, due_day_of_month)
                VALUES (
                    $id, $name, $kind, $balance, $apr, $minimum,
                    $promoRate, $promoEnds, $owner, $dueDay);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(debt.Id));
            command.Parameters.AddWithValue("$name", debt.Name);
            command.Parameters.AddWithValue("$kind", (int)debt.Kind);
            command.Parameters.AddWithValue("$balance", SqliteValues.ToText(debt.Balance));
            command.Parameters.AddWithValue("$apr", SqliteValues.ToText(debt.AnnualPercentageRate));
            command.Parameters.AddWithValue("$minimum", SqliteValues.ToText(debt.MinimumPayment));
            command.Parameters.AddWithValue("$promoRate", SqliteValues.ToNullable(debt.PromotionalRate));
            command.Parameters.AddWithValue("$promoEnds", SqliteValues.ToNullable(debt.PromotionalRateEnds));
            command.Parameters.AddWithValue("$owner", SqliteValues.ToNullable(debt.OwnerMemberId));
            command.Parameters.AddWithValue("$dueDay", SqliteValues.ToNullable(debt.DueDayOfMonth));
            command.ExecuteNonQuery();
        }
    }

    private static List<SavingsFund> LoadFunds(SqliteConnection connection)
    {
        var funds = new List<SavingsFund>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM savings_fund ORDER BY priority DESC, name;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            funds.Add(new SavingsFund
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Purpose = SqliteValues.GetEnum<FundPurpose>(reader, "purpose"),
                CurrentBalance = SqliteValues.GetMoney(reader, "current_balance"),
                TargetAmount = SqliteValues.GetNullableMoney(reader, "target_amount"),
                TargetDate = SqliteValues.GetNullableDate(reader, "target_date"),
                PlannedContribution = SqliteValues.GetMoney(reader, "planned_contribution"),
                ContributionFrequency = SqliteValues.GetEnum<Frequency>(reader, "contribution_frequency"),
                Priority = SqliteValues.GetInt(reader, "priority"),
                OwnerMemberId = SqliteValues.GetNullableGuid(reader, "owner_member_id"),
                IsRevolving = SqliteValues.GetBool(reader, "is_revolving"),
                Notes = SqliteValues.GetNullableString(reader, "notes")
            });
        }

        return funds;
    }

    private static void SaveFunds(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<SavingsFund> funds)
    {
        foreach (var fund in funds)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO savings_fund (
                    id, name, purpose, current_balance, target_amount, target_date,
                    planned_contribution, contribution_frequency, priority,
                    owner_member_id, is_revolving, notes)
                VALUES (
                    $id, $name, $purpose, $balance, $target, $date,
                    $contribution, $frequency, $priority, $owner, $revolving, $notes);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(fund.Id));
            command.Parameters.AddWithValue("$name", fund.Name);
            command.Parameters.AddWithValue("$purpose", (int)fund.Purpose);
            command.Parameters.AddWithValue("$balance", SqliteValues.ToText(fund.CurrentBalance));
            command.Parameters.AddWithValue("$target", SqliteValues.ToNullable(fund.TargetAmount));
            command.Parameters.AddWithValue("$date", SqliteValues.ToNullable(fund.TargetDate));
            command.Parameters.AddWithValue("$contribution", SqliteValues.ToText(fund.PlannedContribution));
            command.Parameters.AddWithValue("$frequency", (int)fund.ContributionFrequency);
            command.Parameters.AddWithValue("$priority", fund.Priority);
            command.Parameters.AddWithValue("$owner", SqliteValues.ToNullable(fund.OwnerMemberId));
            command.Parameters.AddWithValue("$revolving", fund.IsRevolving ? 1 : 0);
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(fund.Notes));
            command.ExecuteNonQuery();
        }
    }

    private static List<Goal> LoadGoals(SqliteConnection connection)
    {
        var goals = new List<Goal>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM goal ORDER BY priority DESC, target_date;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            goals.Add(new Goal
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                Name = reader.GetString(reader.GetOrdinal("name")),
                TargetAmount = SqliteValues.GetMoney(reader, "target_amount"),
                TargetDate = SqliteValues.GetDate(reader, "target_date"),
                CurrentAmount = SqliteValues.GetMoney(reader, "current_amount"),
                Priority = SqliteValues.GetEnum<GoalPriority>(reader, "priority"),
                Scope = SqliteValues.GetEnum<GoalScope>(reader, "scope"),
                OwnerMemberId = SqliteValues.GetNullableGuid(reader, "owner_member_id"),
                PlannedContribution = SqliteValues.GetMoney(reader, "planned_contribution"),
                ContributionFrequency = SqliteValues.GetEnum<Frequency>(reader, "contribution_frequency"),
                SourceScenarioId = SqliteValues.GetNullableGuid(reader, "source_scenario_id"),
                Notes = SqliteValues.GetNullableString(reader, "notes")
            });
        }

        return goals;
    }

    private static void SaveGoals(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<Goal> goals)
    {
        foreach (var goal in goals)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO goal (
                    id, name, target_amount, target_date, current_amount, priority, scope,
                    owner_member_id, planned_contribution, contribution_frequency,
                    source_scenario_id, notes)
                VALUES (
                    $id, $name, $target, $date, $current, $priority, $scope,
                    $owner, $contribution, $frequency, $scenario, $notes);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(goal.Id));
            command.Parameters.AddWithValue("$name", goal.Name);
            command.Parameters.AddWithValue("$target", SqliteValues.ToText(goal.TargetAmount));
            command.Parameters.AddWithValue("$date", SqliteValues.ToText(goal.TargetDate));
            command.Parameters.AddWithValue("$current", SqliteValues.ToText(goal.CurrentAmount));
            command.Parameters.AddWithValue("$priority", (int)goal.Priority);
            command.Parameters.AddWithValue("$scope", (int)goal.Scope);
            command.Parameters.AddWithValue("$owner", SqliteValues.ToNullable(goal.OwnerMemberId));
            command.Parameters.AddWithValue("$contribution", SqliteValues.ToText(goal.PlannedContribution));
            command.Parameters.AddWithValue("$frequency", (int)goal.ContributionFrequency);
            command.Parameters.AddWithValue("$scenario", SqliteValues.ToNullable(goal.SourceScenarioId));
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(goal.Notes));
            command.ExecuteNonQuery();
        }
    }

    #endregion

    #region Scenarios

    private static List<Scenario> LoadScenarios(SqliteConnection connection)
    {
        var lines = LoadScenarioLines(connection);
        var scenarios = new List<Scenario>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM scenario ORDER BY created_utc DESC;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var id = SqliteValues.GetGuid(reader, "id");

            scenarios.Add(new Scenario
            {
                Id = id,
                Name = reader.GetString(reader.GetOrdinal("name")),
                Kind = SqliteValues.GetEnum<ScenarioKind>(reader, "kind"),
                ProposedStartDate = SqliteValues.GetDate(reader, "proposed_start_date"),
                AdvertisedAmount = SqliteValues.GetMoney(reader, "advertised_amount"),
                AdvertisedFrequency = SqliteValues.GetEnum<Frequency>(reader, "advertised_frequency"),
                Status = SqliteValues.GetEnum<ScenarioStatus>(reader, "status"),
                WorstCaseUpliftPercent = SqliteValues.GetDecimal(reader, "worst_case_uplift_percent"),
                BestCaseReductionPercent = SqliteValues.GetDecimal(reader, "best_case_reduction_percent"),
                Notes = SqliteValues.GetNullableString(reader, "notes"),
                OverrideReason = SqliteValues.GetNullableString(reader, "override_reason"),
                Lines = lines.TryGetValue(id, out var scenarioLines) ? scenarioLines : []
            });
        }

        return scenarios;
    }

    private static Dictionary<Guid, List<CostLine>> LoadScenarioLines(SqliteConnection connection)
    {
        var result = new Dictionary<Guid, List<CostLine>>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM scenario_line ORDER BY scenario_id, position;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var scenarioId = SqliteValues.GetGuid(reader, "scenario_id");

            if (!result.TryGetValue(scenarioId, out var list))
            {
                list = [];
                result[scenarioId] = list;
            }

            list.Add(new CostLine
            {
                Area = reader.GetString(reader.GetOrdinal("area")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Question = reader.GetString(reader.GetOrdinal("question")),
                State = SqliteValues.GetEnum<CostLineState>(reader, "state"),
                Amount = SqliteValues.GetMoney(reader, "amount"),
                Frequency = SqliteValues.GetEnum<Frequency>(reader, "frequency"),
                TypicalAmount = SqliteValues.GetNullableMoney(reader, "typical_amount"),
                IsCritical = SqliteValues.GetBool(reader, "is_critical"),
                IsNonCash = SqliteValues.GetBool(reader, "is_non_cash"),
                Notes = SqliteValues.GetNullableString(reader, "notes")
            });
        }

        return result;
    }

    private static void SaveScenarios(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<Scenario> scenarios)
    {
        var created = DateTimeOffset.UtcNow;

        for (var index = 0; index < scenarios.Count; index++)
        {
            var scenario = scenarios[index];

            using (var command = Command(connection, transaction, """
                INSERT INTO scenario (
                    id, name, kind, proposed_start_date, advertised_amount, advertised_frequency,
                    status, worst_case_uplift_percent, best_case_reduction_percent,
                    notes, override_reason, created_utc)
                VALUES (
                    $id, $name, $kind, $start, $advertised, $frequency,
                    $status, $worst, $best, $notes, $override, $created);
                """))
            {
                command.Parameters.AddWithValue("$id", SqliteValues.ToText(scenario.Id));
                command.Parameters.AddWithValue("$name", scenario.Name);
                command.Parameters.AddWithValue("$kind", (int)scenario.Kind);
                command.Parameters.AddWithValue("$start", SqliteValues.ToText(scenario.ProposedStartDate));
                command.Parameters.AddWithValue("$advertised", SqliteValues.ToText(scenario.AdvertisedAmount));
                command.Parameters.AddWithValue("$frequency", (int)scenario.AdvertisedFrequency);
                command.Parameters.AddWithValue("$status", (int)scenario.Status);
                command.Parameters.AddWithValue("$worst", SqliteValues.ToText(scenario.WorstCaseUpliftPercent));
                command.Parameters.AddWithValue("$best", SqliteValues.ToText(scenario.BestCaseReductionPercent));
                command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(scenario.Notes));
                command.Parameters.AddWithValue("$override", SqliteValues.ToNullable(scenario.OverrideReason));

                // Ordering is preserved by offsetting each row, so the list survives a round trip.
                command.Parameters.AddWithValue(
                    "$created",
                    created.AddTicks(-index).ToString("O", CultureInfo.InvariantCulture));

                command.ExecuteNonQuery();
            }

            for (var position = 0; position < scenario.Lines.Count; position++)
            {
                var line = scenario.Lines[position];

                using var command = Command(connection, transaction, """
                    INSERT INTO scenario_line (
                        scenario_id, position, area, name, question, state, amount, frequency,
                        typical_amount, is_critical, is_non_cash, notes)
                    VALUES (
                        $scenario, $position, $area, $name, $question, $state, $amount, $frequency,
                        $typical, $critical, $nonCash, $notes);
                    """);

                command.Parameters.AddWithValue("$scenario", SqliteValues.ToText(scenario.Id));
                command.Parameters.AddWithValue("$position", position);
                command.Parameters.AddWithValue("$area", line.Area);
                command.Parameters.AddWithValue("$name", line.Name);
                command.Parameters.AddWithValue("$question", line.Question);
                command.Parameters.AddWithValue("$state", (int)line.State);
                command.Parameters.AddWithValue("$amount", SqliteValues.ToText(line.Amount));
                command.Parameters.AddWithValue("$frequency", (int)line.Frequency);
                command.Parameters.AddWithValue("$typical", SqliteValues.ToNullable(line.TypicalAmount));
                command.Parameters.AddWithValue("$critical", line.IsCritical ? 1 : 0);
                command.Parameters.AddWithValue("$nonCash", line.IsNonCash ? 1 : 0);
                command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(line.Notes));
                command.ExecuteNonQuery();
            }
        }
    }

    #endregion

    #region Guidance

    /// <summary>0 for a personal discretionary share, 1 for a shared-contribution override.</summary>
    private const int DiscretionaryPercentPurpose = 0;
    private const int SharedOverridePurpose = 1;

    private static AllocationRules LoadAllocationRules(SqliteConnection connection)
    {
        var discretionary = new Dictionary<Guid, decimal>();
        var overrides = new Dictionary<Guid, decimal>();

        using (var percentCommand = connection.CreateCommand())
        {
            percentCommand.CommandText = "SELECT * FROM allocation_member_percent;";
            using var percentReader = percentCommand.ExecuteReader();

            while (percentReader.Read())
            {
                var memberId = SqliteValues.GetGuid(percentReader, "member_id");
                var percent = SqliteValues.GetDecimal(percentReader, "percent");

                if (SqliteValues.GetInt(percentReader, "purpose") == DiscretionaryPercentPurpose)
                {
                    discretionary[memberId] = percent;
                }
                else
                {
                    overrides[memberId] = percent;
                }
            }
        }

        var surplus = new List<SurplusRule>();

        using (var surplusCommand = connection.CreateCommand())
        {
            surplusCommand.CommandText = "SELECT * FROM allocation_surplus_rule ORDER BY position;";
            using var surplusReader = surplusCommand.ExecuteReader();

            while (surplusReader.Read())
            {
                surplus.Add(new SurplusRule
                {
                    Target = SqliteValues.GetEnum<SurplusTarget>(surplusReader, "target"),
                    Percent = SqliteValues.GetDecimal(surplusReader, "percent")
                });
            }
        }

        var reduction = new List<ReducibleBucket>();

        using (var reductionCommand = connection.CreateCommand())
        {
            reductionCommand.CommandText = "SELECT * FROM allocation_reduction_step ORDER BY position;";
            using var reductionReader = reductionCommand.ExecuteReader();

            while (reductionReader.Read())
            {
                reduction.Add(SqliteValues.GetEnum<ReducibleBucket>(reductionReader, "bucket"));
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM allocation_rules WHERE id = 1;";
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return new AllocationRules();
        }

        return new AllocationRules
        {
            Basis = SqliteValues.GetEnum<IncomeBasis>(reader, "basis"),
            OptimisticIncomeMayFundEssentials = SqliteValues.GetBool(reader, "optimistic_funds_essentials"),
            SafetyBuffer = SqliteValues.GetMoney(reader, "safety_buffer"),
            DiscretionaryMethod = SqliteValues.GetEnum<DiscretionaryMethod>(reader, "discretionary_method"),
            DiscretionaryPercent = SqliteValues.GetDecimal(reader, "discretionary_percent"),
            DiscretionaryPercentConfigured = SqliteValues.GetBool(reader, "discretionary_percent_configured"),
            FixedDiscretionaryAmount = SqliteValues.GetMoney(reader, "fixed_discretionary_amount"),
            SharedEntertainmentPerPayday = SqliteValues.GetMoney(reader, "shared_entertainment"),
            FlexibleSavingsPerPayday = SqliteValues.GetMoney(reader, "flexible_savings"),
            CustomDiscretionaryPercents = discretionary,
            SharedContributionOverrides = overrides,
            SurplusRules = surplus,
            ReductionOrder = reduction.Count == 0 ? AllocationRules.DefaultReductionOrder : reduction,
            FundingOrder = LoadFundingOrder(connection),
            RoundingRemainderMemberId = SqliteValues.GetNullableGuid(reader, "rounding_remainder_member_id"),
            Notes = SqliteValues.GetNullableString(reader, "notes")
        };
    }

    private static CostLocality LoadLocality(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM allocation_rules WHERE id = 1;";
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return CostLocality.UnitedStates;
        }

        return new CostLocality
        {
            Country = reader.GetString(reader.GetOrdinal("locality_country")),
            State = SqliteValues.GetNullableString(reader, "locality_state"),
            County = SqliteValues.GetNullableString(reader, "locality_county"),
            City = SqliteValues.GetNullableString(reader, "locality_city")
        };
    }

    private static void SaveAllocationRules(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AllocationRules rules,
        NeedsHierarchy hierarchy,
        CostLocality locality)
    {
        using (var command = Command(connection, transaction, """
            INSERT INTO allocation_rules (
                id, basis, optimistic_funds_essentials, safety_buffer, discretionary_method,
                discretionary_percent, discretionary_percent_configured, fixed_discretionary_amount,
                shared_entertainment,
                flexible_savings, rounding_remainder_member_id, notes,
                fallback_tier_essential, fallback_tier_optional,
                locality_country, locality_state, locality_county, locality_city)
            VALUES (
                1, $basis, $optimistic, $buffer, $method, $percent, $percentConfigured, $fixed, $entertainment,
                $flexible, $remainder, $notes, $fallbackEssential, $fallbackOptional,
                $country, $state, $county, $city);
            """))
        {
            command.Parameters.AddWithValue("$basis", (int)rules.Basis);
            command.Parameters.AddWithValue("$optimistic", rules.OptimisticIncomeMayFundEssentials ? 1 : 0);
            command.Parameters.AddWithValue("$buffer", SqliteValues.ToText(rules.SafetyBuffer));
            command.Parameters.AddWithValue("$method", (int)rules.DiscretionaryMethod);
            command.Parameters.AddWithValue("$percent", SqliteValues.ToText(rules.DiscretionaryPercent));
            command.Parameters.AddWithValue("$percentConfigured", rules.DiscretionaryPercentConfigured ? 1 : 0);
            command.Parameters.AddWithValue("$fixed", SqliteValues.ToText(rules.FixedDiscretionaryAmount));
            command.Parameters.AddWithValue("$entertainment", SqliteValues.ToText(rules.SharedEntertainmentPerPayday));
            command.Parameters.AddWithValue("$flexible", SqliteValues.ToText(rules.FlexibleSavingsPerPayday));
            command.Parameters.AddWithValue("$remainder", SqliteValues.ToNullable(rules.RoundingRemainderMemberId));
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(rules.Notes));
            command.Parameters.AddWithValue("$fallbackEssential", (int)hierarchy.FallbackForEssential);
            command.Parameters.AddWithValue("$fallbackOptional", (int)hierarchy.FallbackForOptional);
            command.Parameters.AddWithValue("$country", locality.Country);
            command.Parameters.AddWithValue("$state", SqliteValues.ToNullable(locality.State));
            command.Parameters.AddWithValue("$county", SqliteValues.ToNullable(locality.County));
            command.Parameters.AddWithValue("$city", SqliteValues.ToNullable(locality.City));
            command.ExecuteNonQuery();
        }

        SavePercents(connection, transaction, rules.CustomDiscretionaryPercents, DiscretionaryPercentPurpose);
        SavePercents(connection, transaction, rules.SharedContributionOverrides, SharedOverridePurpose);

        for (var position = 0; position < rules.SurplusRules.Count; position++)
        {
            var rule = rules.SurplusRules[position];

            using var command = Command(connection, transaction, """
                INSERT INTO allocation_surplus_rule (position, target, percent)
                VALUES ($position, $target, $percent);
                """);

            command.Parameters.AddWithValue("$position", position);
            command.Parameters.AddWithValue("$target", (int)rule.Target);
            command.Parameters.AddWithValue("$percent", SqliteValues.ToText(rule.Percent));
            command.ExecuteNonQuery();
        }

        for (var position = 0; position < rules.ReductionOrder.Count; position++)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO allocation_reduction_step (position, bucket) VALUES ($position, $bucket);
                """);

            command.Parameters.AddWithValue("$position", position);
            command.Parameters.AddWithValue("$bucket", (int)rules.ReductionOrder[position]);
            command.ExecuteNonQuery();
        }

        for (var position = 0; position < rules.FundingOrder.Count; position++)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO allocation_funding_step (position, bucket) VALUES ($position, $bucket);
                """);

            command.Parameters.AddWithValue("$position", position);
            command.Parameters.AddWithValue("$bucket", (int)rules.FundingOrder[position]);
            command.ExecuteNonQuery();
        }
    }

    private static IReadOnlyList<FundingBucket> LoadFundingOrder(SqliteConnection connection)
    {
        var funding = new List<FundingBucket>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM allocation_funding_step ORDER BY position;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            funding.Add(SqliteValues.GetEnum<FundingBucket>(reader, "bucket"));
        }

        return funding.Count == 0 ? AllocationRules.DefaultFundingOrder : funding;
    }

    private static void SavePercents(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyDictionary<Guid, decimal> percents,
        int purpose)
    {
        foreach (var (memberId, percent) in percents)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO allocation_member_percent (member_id, purpose, percent)
                VALUES ($member, $purpose, $percent);
                """);

            command.Parameters.AddWithValue("$member", SqliteValues.ToText(memberId));
            command.Parameters.AddWithValue("$purpose", purpose);
            command.Parameters.AddWithValue("$percent", SqliteValues.ToText(percent));
            command.ExecuteNonQuery();
        }
    }

    private static NeedsHierarchy LoadHierarchy(SqliteConnection connection)
    {
        var rules = new List<NeedCategoryRule>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM needs_category_rule ORDER BY tier, category_name;";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                rules.Add(new NeedCategoryRule
                {
                    CategoryName = reader.GetString(reader.GetOrdinal("category_name")),
                    Tier = SqliteValues.GetEnum<NeedTier>(reader, "tier"),
                    IsAlwaysProtected = SqliteValues.GetBool(reader, "is_always_protected"),
                    Notes = SqliteValues.GetNullableString(reader, "notes")
                });
            }
        }

        if (rules.Count == 0)
        {
            return NeedsHierarchy.Default;
        }

        using var fallbackCommand = connection.CreateCommand();
        fallbackCommand.CommandText = "SELECT * FROM allocation_rules WHERE id = 1;";
        using var fallbackReader = fallbackCommand.ExecuteReader();

        if (!fallbackReader.Read())
        {
            return new NeedsHierarchy { Rules = rules };
        }

        return new NeedsHierarchy
        {
            Rules = rules,
            FallbackForEssential = SqliteValues.GetEnum<NeedTier>(fallbackReader, "fallback_tier_essential"),
            FallbackForOptional = SqliteValues.GetEnum<NeedTier>(fallbackReader, "fallback_tier_optional")
        };
    }

    private static void SaveHierarchy(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NeedsHierarchy hierarchy)
    {
        foreach (var rule in hierarchy.Rules)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO needs_category_rule (category_name, tier, is_always_protected, notes)
                VALUES ($category, $tier, $protectedAlways, $notes);
                """);

            command.Parameters.AddWithValue("$category", rule.CategoryName);
            command.Parameters.AddWithValue("$tier", (int)rule.Tier);
            command.Parameters.AddWithValue("$protectedAlways", rule.IsAlwaysProtected ? 1 : 0);
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(rule.Notes));
            command.ExecuteNonQuery();
        }
    }

    private static List<GroceryPlan> LoadGroceryPlans(SqliteConnection connection)
    {
        var categoriesByPlan = new Dictionary<Guid, List<GroceryCategoryPlan>>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM grocery_category ORDER BY name;";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var planId = SqliteValues.GetGuid(reader, "plan_id");

                if (!categoriesByPlan.TryGetValue(planId, out var list))
                {
                    list = [];
                    categoriesByPlan[planId] = list;
                }

                list.Add(new GroceryCategoryPlan
                {
                    Id = SqliteValues.GetGuid(reader, "id"),
                    Name = reader.GetString(reader.GetOrdinal("name")),
                    IsEssential = SqliteValues.GetBool(reader, "is_essential"),
                    SuggestedWeekly = SqliteValues.GetMoney(reader, "suggested_weekly"),
                    LowRange = SqliteValues.GetMoney(reader, "low_range"),
                    TypicalRange = SqliteValues.GetMoney(reader, "typical_range"),
                    ComfortableRange = SqliteValues.GetMoney(reader, "comfortable_range"),
                    WeeklyLimit = SqliteValues.GetMoney(reader, "weekly_limit"),
                    CarriedForward = SqliteValues.GetMoney(reader, "carried_forward"),
                    Rollover = SqliteValues.GetEnum<RolloverRule>(reader, "rollover"),
                    SuppliedByAssistance = SqliteValues.GetBool(reader, "supplied_by_assistance"),
                    GuidanceSource = reader.GetString(reader.GetOrdinal("guidance_source")),
                    GuidanceEffectiveDate = SqliteValues.GetNullableDate(reader, "guidance_effective_date"),
                    Confidence = SqliteValues.GetEnum<GuidanceConfidence>(reader, "confidence"),
                    ReviewRequired = SqliteValues.GetBool(reader, "review_required"),
                    Notes = SqliteValues.GetNullableString(reader, "notes")
                });
            }
        }

        var suppliedByPlan = new Dictionary<Guid, List<string>>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM grocery_plan_assistance_category ORDER BY category_name;";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var planId = SqliteValues.GetGuid(reader, "plan_id");

                if (!suppliedByPlan.TryGetValue(planId, out var list))
                {
                    list = [];
                    suppliedByPlan[planId] = list;
                }

                list.Add(reader.GetString(reader.GetOrdinal("category_name")));
            }
        }

        var plans = new List<GroceryPlan>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM grocery_plan ORDER BY kind;";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var id = SqliteValues.GetGuid(reader, "id");

                plans.Add(new GroceryPlan
                {
                    Id = id,
                    Kind = SqliteValues.GetEnum<GroceryPlanKind>(reader, "kind"),
                    Name = reader.GetString(reader.GetOrdinal("name")),
                    Notes = SqliteValues.GetNullableString(reader, "notes"),
                    CashAmountNeedsConfirmation = SqliteValues.GetBool(reader, "cash_amount_needs_confirmation"),
                    Categories = categoriesByPlan.TryGetValue(id, out var categories) ? categories : [],
                    Assistance = new FoodAssistance
                    {
                        IsExpected = SqliteValues.GetBool(reader, "assistance_is_expected"),
                        IsSuspended = SqliteValues.GetBool(reader, "assistance_is_suspended"),
                        SourceName = SqliteValues.GetNullableString(reader, "assistance_source"),
                        EstimatedWeeklyValue = SqliteValues.GetMoney(reader, "assistance_weekly_value"),
                        EffectiveDate = SqliteValues.GetNullableDate(reader, "assistance_effective_date"),
                        ReviewDate = SqliteValues.GetNullableDate(reader, "assistance_review_date"),
                        Notes = SqliteValues.GetNullableString(reader, "assistance_notes"),
                        CategoriesSupplied = suppliedByPlan.TryGetValue(id, out var supplied) ? supplied : []
                    }
                });
            }
        }

        return plans;
    }

    private static void SaveGroceryPlans(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<GroceryPlan> plans)
    {
        foreach (var plan in plans)
        {
            using (var command = Command(connection, transaction, """
                INSERT INTO grocery_plan (
                    id, kind, name, notes, cash_amount_needs_confirmation, assistance_is_expected,
                    assistance_is_suspended,
                    assistance_source, assistance_weekly_value, assistance_effective_date,
                    assistance_review_date, assistance_notes)
                VALUES (
                    $id, $kind, $name, $notes, $cashConfirm, $expected, $suspended, $source, $value,
                    $effective, $review, $assistanceNotes);
                """))
            {
                command.Parameters.AddWithValue("$id", SqliteValues.ToText(plan.Id));
                command.Parameters.AddWithValue("$kind", (int)plan.Kind);
                command.Parameters.AddWithValue("$name", plan.Name);
                command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(plan.Notes));
                command.Parameters.AddWithValue("$cashConfirm", plan.CashAmountNeedsConfirmation ? 1 : 0);
                command.Parameters.AddWithValue("$expected", plan.Assistance.IsExpected ? 1 : 0);
                command.Parameters.AddWithValue("$suspended", plan.Assistance.IsSuspended ? 1 : 0);
                command.Parameters.AddWithValue("$source", SqliteValues.ToNullable(plan.Assistance.SourceName));
                command.Parameters.AddWithValue(
                    "$value",
                    SqliteValues.ToText(plan.Assistance.EstimatedWeeklyValue));
                command.Parameters.AddWithValue(
                    "$effective",
                    SqliteValues.ToNullable(plan.Assistance.EffectiveDate));
                command.Parameters.AddWithValue("$review", SqliteValues.ToNullable(plan.Assistance.ReviewDate));
                command.Parameters.AddWithValue(
                    "$assistanceNotes",
                    SqliteValues.ToNullable(plan.Assistance.Notes));
                command.ExecuteNonQuery();
            }

            foreach (var supplied in plan.Assistance.CategoriesSupplied.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                using var command = Command(connection, transaction, """
                    INSERT INTO grocery_plan_assistance_category (plan_id, category_name)
                    VALUES ($plan, $category);
                    """);

                command.Parameters.AddWithValue("$plan", SqliteValues.ToText(plan.Id));
                command.Parameters.AddWithValue("$category", supplied);
                command.ExecuteNonQuery();
            }

            foreach (var category in plan.Categories)
            {
                using var command = Command(connection, transaction, """
                    INSERT INTO grocery_category (
                        id, plan_id, name, is_essential, suggested_weekly, low_range, typical_range,
                        comfortable_range, weekly_limit, carried_forward, rollover,
                        supplied_by_assistance, guidance_source, guidance_effective_date,
                        confidence, review_required, notes)
                    VALUES (
                        $id, $plan, $name, $essential, $suggested, $low, $typical, $comfortable,
                        $limit, $carried, $rollover, $supplied, $source, $effective, $confidence,
                        $review, $notes);
                    """);

                command.Parameters.AddWithValue("$id", SqliteValues.ToText(category.Id));
                command.Parameters.AddWithValue("$plan", SqliteValues.ToText(plan.Id));
                command.Parameters.AddWithValue("$name", category.Name);
                command.Parameters.AddWithValue("$essential", category.IsEssential ? 1 : 0);
                command.Parameters.AddWithValue("$suggested", SqliteValues.ToText(category.SuggestedWeekly));
                command.Parameters.AddWithValue("$low", SqliteValues.ToText(category.LowRange));
                command.Parameters.AddWithValue("$typical", SqliteValues.ToText(category.TypicalRange));
                command.Parameters.AddWithValue("$comfortable", SqliteValues.ToText(category.ComfortableRange));
                command.Parameters.AddWithValue("$limit", SqliteValues.ToText(category.WeeklyLimit));
                command.Parameters.AddWithValue("$carried", SqliteValues.ToText(category.CarriedForward));
                command.Parameters.AddWithValue("$rollover", (int)category.Rollover);
                command.Parameters.AddWithValue("$supplied", category.SuppliedByAssistance ? 1 : 0);
                command.Parameters.AddWithValue("$source", category.GuidanceSource);
                command.Parameters.AddWithValue(
                    "$effective",
                    SqliteValues.ToNullable(category.GuidanceEffectiveDate));
                command.Parameters.AddWithValue("$confidence", (int)category.Confidence);
                command.Parameters.AddWithValue("$review", category.ReviewRequired ? 1 : 0);
                command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(category.Notes));
                command.ExecuteNonQuery();
            }
        }
    }

    private static List<CostGuidanceRecord> LoadCostGuidance(SqliteConnection connection)
    {
        var records = new List<CostGuidanceRecord>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM cost_guidance ORDER BY category, effective_date DESC;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            records.Add(new CostGuidanceRecord
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                Locality = new CostLocality
                {
                    Country = reader.GetString(reader.GetOrdinal("country")),
                    State = SqliteValues.GetNullableString(reader, "state"),
                    County = SqliteValues.GetNullableString(reader, "county"),
                    City = SqliteValues.GetNullableString(reader, "city")
                },
                Composition = new HouseholdComposition(
                    SqliteValues.GetInt(reader, "adults"),
                    SqliteValues.GetInt(reader, "children")),
                Category = reader.GetString(reader.GetOrdinal("category")),
                EffectiveDate = SqliteValues.GetDate(reader, "effective_date"),
                SourceName = reader.GetString(reader.GetOrdinal("source_name")),
                SourceType = SqliteValues.GetEnum<CostGuidanceSourceType>(reader, "source_type"),
                ObservedOn = SqliteValues.GetNullableDate(reader, "observed_on"),
                Low = SqliteValues.GetMoney(reader, "low_range"),
                Typical = SqliteValues.GetMoney(reader, "typical_range"),
                Comfortable = SqliteValues.GetMoney(reader, "comfortable_range"),
                Confidence = SqliteValues.GetEnum<GuidanceConfidence>(reader, "confidence"),
                LastReviewedOn = SqliteValues.GetNullableDate(reader, "last_reviewed_on"),
                ReviewByDate = SqliteValues.GetNullableDate(reader, "review_by_date"),
                Notes = SqliteValues.GetNullableString(reader, "notes"),
                SourceUrl = SqliteValues.GetNullableString(reader, "source_url"),
                SourceIdentifier = SqliteValues.GetNullableString(reader, "source_identifier"),
                Assumptions = SqliteValues.GetNullableString(reader, "assumptions"),
                UnitOrFrequency = SqliteValues.GetNullableString(reader, "unit_or_frequency") ?? "Weekly, household",
                IsDerived = SqliteValues.GetBool(reader, "is_derived"),
                SourceTotal = SqliteValues.GetNullableMoney(reader, "source_total"),
                AllocationMethod = SqliteValues.GetNullableString(reader, "allocation_method"),
                Calculation = SqliteValues.GetNullableString(reader, "calculation"),
                PackVersion = SqliteValues.GetNullableString(reader, "pack_version"),
                UserSelectedAmount = SqliteValues.GetNullableMoney(reader, "user_selected_amount"),
                PriceKind = SqliteValues.GetEnum<PriceKind>(reader, "price_kind"),
                FreshnessDays = SqliteValues.GetInt(reader, "freshness_days")
            });
        }

        return records;
    }

    private static void SaveCostGuidance(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<CostGuidanceRecord> records)
    {
        foreach (var record in records)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO cost_guidance (
                    id, country, state, county, city, adults, children, category, effective_date,
                    source_name, source_type, observed_on, low_range, typical_range,
                    comfortable_range, confidence, last_reviewed_on, review_by_date, notes,
                    source_url, source_identifier, assumptions, unit_or_frequency, is_derived,
                    source_total, allocation_method, calculation, pack_version, user_selected_amount,
                    price_kind, freshness_days)
                VALUES (
                    $id, $country, $state, $county, $city, $adults, $children, $category,
                    $effective, $source, $sourceType, $observed, $low, $typical, $comfortable,
                    $confidence, $reviewed, $reviewBy, $notes, $url, $identifier, $assumptions,
                    $unit, $derived, $sourceTotal, $method, $calculation, $pack, $chosen,
                    $priceKind, $freshness);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(record.Id));
            command.Parameters.AddWithValue("$country", record.Locality.Country);
            command.Parameters.AddWithValue("$state", SqliteValues.ToNullable(record.Locality.State));
            command.Parameters.AddWithValue("$county", SqliteValues.ToNullable(record.Locality.County));
            command.Parameters.AddWithValue("$city", SqliteValues.ToNullable(record.Locality.City));
            command.Parameters.AddWithValue("$adults", record.Composition.Adults);
            command.Parameters.AddWithValue("$children", record.Composition.Children);
            command.Parameters.AddWithValue("$category", record.Category);
            command.Parameters.AddWithValue("$effective", SqliteValues.ToText(record.EffectiveDate));
            command.Parameters.AddWithValue("$source", record.SourceName);
            command.Parameters.AddWithValue("$sourceType", (int)record.SourceType);
            command.Parameters.AddWithValue("$observed", SqliteValues.ToNullable(record.ObservedOn));
            command.Parameters.AddWithValue("$low", SqliteValues.ToText(record.Low));
            command.Parameters.AddWithValue("$typical", SqliteValues.ToText(record.Typical));
            command.Parameters.AddWithValue("$comfortable", SqliteValues.ToText(record.Comfortable));
            command.Parameters.AddWithValue("$confidence", (int)record.Confidence);
            command.Parameters.AddWithValue("$reviewed", SqliteValues.ToNullable(record.LastReviewedOn));
            command.Parameters.AddWithValue("$reviewBy", SqliteValues.ToNullable(record.ReviewByDate));
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(record.Notes));
            command.Parameters.AddWithValue("$url", SqliteValues.ToNullable(record.SourceUrl));
            command.Parameters.AddWithValue("$identifier", SqliteValues.ToNullable(record.SourceIdentifier));
            command.Parameters.AddWithValue("$assumptions", SqliteValues.ToNullable(record.Assumptions));
            command.Parameters.AddWithValue("$unit", record.UnitOrFrequency);
            command.Parameters.AddWithValue("$derived", record.IsDerived ? 1 : 0);
            command.Parameters.AddWithValue("$sourceTotal", SqliteValues.ToNullable(record.SourceTotal));
            command.Parameters.AddWithValue("$method", SqliteValues.ToNullable(record.AllocationMethod));
            command.Parameters.AddWithValue("$calculation", SqliteValues.ToNullable(record.Calculation));
            command.Parameters.AddWithValue("$pack", SqliteValues.ToNullable(record.PackVersion));
            command.Parameters.AddWithValue("$chosen", SqliteValues.ToNullable(record.UserSelectedAmount));
            command.Parameters.AddWithValue("$priceKind", (int)record.PriceKind);
            command.Parameters.AddWithValue("$freshness", record.FreshnessDays);
            command.ExecuteNonQuery();
        }
    }

    private static List<ObligationReserve> LoadReserves(SqliteConnection connection)
    {
        var reserves = new List<ObligationReserve>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM obligation_reserve;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            reserves.Add(new ObligationReserve
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                ObligationId = SqliteValues.GetGuid(reader, "obligation_id"),
                Kind = SqliteValues.GetEnum<ObligationKind>(reader, "kind"),
                Reserved = SqliteValues.GetMoney(reader, "reserved"),
                UpdatedOn = SqliteValues.GetDate(reader, "updated_on"),
                IsProtected = SqliteValues.GetBool(reader, "is_protected"),
                Notes = SqliteValues.GetNullableString(reader, "notes")
            });
        }

        return reserves;
    }

    private static void SaveReserves(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<ObligationReserve> reserves)
    {
        foreach (var reserve in reserves)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO obligation_reserve (
                    id, obligation_id, kind, reserved, updated_on, is_protected, notes)
                VALUES ($id, $obligation, $kind, $reserved, $updated, $protectedFlag, $notes);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(reserve.Id));
            command.Parameters.AddWithValue("$obligation", SqliteValues.ToText(reserve.ObligationId));
            command.Parameters.AddWithValue("$kind", (int)reserve.Kind);
            command.Parameters.AddWithValue("$reserved", SqliteValues.ToText(reserve.Reserved));
            command.Parameters.AddWithValue("$updated", SqliteValues.ToText(reserve.UpdatedOn));
            command.Parameters.AddWithValue("$protectedFlag", reserve.IsProtected ? 1 : 0);
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(reserve.Notes));
            command.ExecuteNonQuery();
        }
    }

    private static List<PersonalTransfer> LoadTransfers(SqliteConnection connection)
    {
        var transfers = new List<PersonalTransfer>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM personal_transfer ORDER BY transfer_date DESC;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            transfers.Add(new PersonalTransfer
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                FromMemberId = SqliteValues.GetGuid(reader, "from_member_id"),
                ToMemberId = SqliteValues.GetGuid(reader, "to_member_id"),
                Amount = SqliteValues.GetMoney(reader, "amount"),
                Date = SqliteValues.GetDate(reader, "transfer_date"),
                Purpose = SqliteValues.GetNullableString(reader, "purpose"),
                IsRecurring = SqliteValues.GetBool(reader, "is_recurring"),
                RecurringFrequency = SqliteValues.GetEnum<Frequency>(reader, "recurring_frequency")
            });
        }

        return transfers;
    }

    private static void SaveTransfers(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<PersonalTransfer> transfers)
    {
        foreach (var transfer in transfers)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO personal_transfer (
                    id, from_member_id, to_member_id, amount, transfer_date, purpose,
                    is_recurring, recurring_frequency)
                VALUES ($id, $from, $to, $amount, $date, $purpose, $recurring, $frequency);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(transfer.Id));
            command.Parameters.AddWithValue("$from", SqliteValues.ToText(transfer.FromMemberId));
            command.Parameters.AddWithValue("$to", SqliteValues.ToText(transfer.ToMemberId));
            command.Parameters.AddWithValue("$amount", SqliteValues.ToText(transfer.Amount));
            command.Parameters.AddWithValue("$date", SqliteValues.ToText(transfer.Date));
            command.Parameters.AddWithValue("$purpose", SqliteValues.ToNullable(transfer.Purpose));
            command.Parameters.AddWithValue("$recurring", transfer.IsRecurring ? 1 : 0);
            command.Parameters.AddWithValue("$frequency", (int)transfer.RecurringFrequency);
            command.ExecuteNonQuery();
        }
    }

    private static List<ReimbursementOffset> LoadReimbursementOffsets(SqliteConnection connection)
    {
        var offsets = new List<ReimbursementOffset>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM reimbursement_offset;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            offsets.Add(new ReimbursementOffset
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                IncomeSourceId = SqliteValues.GetGuid(reader, "income_source_id"),
                ObligationId = SqliteValues.GetNullableGuid(reader, "obligation_id"),
                Kind = SqliteValues.GetEnum<ObligationKind>(reader, "kind"),
                OffsetPerPeriod = SqliteValues.GetMoney(reader, "offset_per_period"),
                Notes = SqliteValues.GetNullableString(reader, "notes")
            });
        }

        return offsets;
    }

    private static void SaveReimbursementOffsets(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<ReimbursementOffset> offsets)
    {
        foreach (var offset in offsets)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO reimbursement_offset (
                    id, income_source_id, obligation_id, kind, offset_per_period, notes)
                VALUES ($id, $source, $obligation, $kind, $amount, $notes);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(offset.Id));
            command.Parameters.AddWithValue("$source", SqliteValues.ToText(offset.IncomeSourceId));
            command.Parameters.AddWithValue("$obligation", SqliteValues.ToNullable(offset.ObligationId));
            command.Parameters.AddWithValue("$kind", (int)offset.Kind);
            command.Parameters.AddWithValue("$amount", SqliteValues.ToText(offset.OffsetPerPeriod));
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(offset.Notes));
            command.ExecuteNonQuery();
        }
    }

    private static List<Product> LoadProducts(SqliteConnection connection)
    {
        var substitutes = new Dictionary<Guid, List<Guid>>();

        using (var sub = connection.CreateCommand())
        {
            sub.CommandText = "SELECT * FROM product_substitute;";
            using var reader = sub.ExecuteReader();

            while (reader.Read())
            {
                var id = SqliteValues.GetGuid(reader, "product_id");
                if (!substitutes.TryGetValue(id, out var list))
                {
                    list = [];
                    substitutes[id] = list;
                }

                list.Add(SqliteValues.GetGuid(reader, "substitute_id"));
            }
        }

        var products = new List<Product>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM product ORDER BY name;";
        using var productReader = command.ExecuteReader();

        while (productReader.Read())
        {
            var id = SqliteValues.GetGuid(productReader, "id");
            products.Add(new Product
            {
                Id = id,
                Name = productReader.GetString(productReader.GetOrdinal("name")),
                Category = productReader.GetString(productReader.GetOrdinal("category")),
                Subcategory = SqliteValues.GetNullableString(productReader, "subcategory"),
                Brand = SqliteValues.GetNullableString(productReader, "brand"),
                Description = SqliteValues.GetNullableString(productReader, "description"),
                PackageSize = SqliteValues.GetDecimal(productReader, "package_size"),
                Quantity = SqliteValues.GetDecimal(productReader, "quantity"),
                UnitOfMeasure = productReader.GetString(productReader.GetOrdinal("unit_of_measure")),
                UnitsPerPackage = SqliteValues.GetDecimal(productReader, "units_per_package"),
                UpcOrSku = SqliteValues.GetNullableString(productReader, "upc_or_sku"),
                IsEssential = SqliteValues.GetBool(productReader, "is_essential"),
                Tier = SqliteValues.GetEnum<NeedTier>(productReader, "tier"),
                PurchaseFrequency = SqliteValues.GetEnum<Frequency>(productReader, "purchase_frequency"),
                IsPreferred = SqliteValues.GetBool(productReader, "is_preferred"),
                IsActive = SqliteValues.GetBool(productReader, "is_active"),
                TaxCategory = SqliteValues.GetEnum<ProductTaxCategory>(productReader, "tax_category"),
                Notes = SqliteValues.GetNullableString(productReader, "notes"),
                SubstituteProductIds = substitutes.TryGetValue(id, out var ids) ? ids : []
            });
        }

        return products;
    }

    private static void SaveProducts(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<Product> products)
    {
        foreach (var product in products)
        {
            using (var command = Command(connection, transaction, """
                INSERT INTO product (
                    id, name, category, subcategory, brand, description, package_size, quantity,
                    unit_of_measure, units_per_package, upc_or_sku, is_essential, tier,
                    purchase_frequency, is_preferred, is_active, tax_category, notes)
                VALUES (
                    $id, $name, $category, $sub, $brand, $description, $size, $qty, $unit, $units,
                    $upc, $essential, $tier, $freq, $preferred, $active, $tax, $notes);
                """))
            {
                command.Parameters.AddWithValue("$id", SqliteValues.ToText(product.Id));
                command.Parameters.AddWithValue("$name", product.Name);
                command.Parameters.AddWithValue("$category", product.Category);
                command.Parameters.AddWithValue("$sub", SqliteValues.ToNullable(product.Subcategory));
                command.Parameters.AddWithValue("$brand", SqliteValues.ToNullable(product.Brand));
                command.Parameters.AddWithValue("$description", SqliteValues.ToNullable(product.Description));
                command.Parameters.AddWithValue("$size", SqliteValues.ToText(product.PackageSize));
                command.Parameters.AddWithValue("$qty", SqliteValues.ToText(product.Quantity));
                command.Parameters.AddWithValue("$unit", product.UnitOfMeasure);
                command.Parameters.AddWithValue("$units", SqliteValues.ToText(product.UnitsPerPackage));
                command.Parameters.AddWithValue("$upc", SqliteValues.ToNullable(product.UpcOrSku));
                command.Parameters.AddWithValue("$essential", product.IsEssential ? 1 : 0);
                command.Parameters.AddWithValue("$tier", (int)product.Tier);
                command.Parameters.AddWithValue("$freq", (int)product.PurchaseFrequency);
                command.Parameters.AddWithValue("$preferred", product.IsPreferred ? 1 : 0);
                command.Parameters.AddWithValue("$active", product.IsActive ? 1 : 0);
                command.Parameters.AddWithValue("$tax", (int)product.TaxCategory);
                command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(product.Notes));
                command.ExecuteNonQuery();
            }
        }

        foreach (var product in products)
        {
            foreach (var substitute in product.SubstituteProductIds)
            {
                using var command = Command(connection, transaction, """
                    INSERT INTO product_substitute (product_id, substitute_id) VALUES ($id, $sub);
                    """);
                command.Parameters.AddWithValue("$id", SqliteValues.ToText(product.Id));
                command.Parameters.AddWithValue("$sub", SqliteValues.ToText(substitute));
                command.ExecuteNonQuery();
            }
        }
    }

    private static List<PriceObservation> LoadPriceObservations(SqliteConnection connection)
    {
        var rows = new List<PriceObservation>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM price_observation ORDER BY observed_on DESC;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(new PriceObservation
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                ProductId = SqliteValues.GetGuid(reader, "product_id"),
                Retailer = SqliteValues.GetNullableString(reader, "retailer"),
                StoreLocation = SqliteValues.GetNullableString(reader, "store_location"),
                Locality = new CostLocality
                {
                    Country = reader.GetString(reader.GetOrdinal("country")),
                    State = SqliteValues.GetNullableString(reader, "state"),
                    County = SqliteValues.GetNullableString(reader, "county"),
                    City = SqliteValues.GetNullableString(reader, "city")
                },
                ShelfPrice = SqliteValues.GetMoney(reader, "shelf_price"),
                LoyaltyOrSalePrice = SqliteValues.GetNullableMoney(reader, "loyalty_or_sale_price"),
                TaxAmount = SqliteValues.GetNullableMoney(reader, "tax_amount"),
                DepositOrFee = SqliteValues.GetNullableMoney(reader, "deposit_or_fee"),
                EstimatedCheckoutPrice = SqliteValues.GetNullableMoney(reader, "estimated_checkout_price"),
                ActualCheckoutPrice = SqliteValues.GetNullableMoney(reader, "actual_checkout_price"),
                ObservedOn = SqliteValues.GetDate(reader, "observed_on"),
                SaleStartsOn = SqliteValues.GetNullableDate(reader, "sale_starts_on"),
                SaleEndsOn = SqliteValues.GetNullableDate(reader, "sale_ends_on"),
                SourceName = reader.GetString(reader.GetOrdinal("source_name")),
                SourceType = SqliteValues.GetEnum<CostGuidanceSourceType>(reader, "source_type"),
                Confidence = SqliteValues.GetEnum<GuidanceConfidence>(reader, "confidence"),
                PriceKind = SqliteValues.GetEnum<PriceKind>(reader, "price_kind"),
                ReviewByDate = SqliteValues.GetNullableDate(reader, "review_by_date"),
                TaxCategory = SqliteValues.GetEnum<ProductTaxCategory>(reader, "tax_category"),
                TaxCollection = SqliteValues.GetEnum<TaxCollectionMethod>(reader, "tax_collection"),
                ExciseEmbeddedInShelfPrice = SqliteValues.GetBool(reader, "excise_embedded"),
                Notes = SqliteValues.GetNullableString(reader, "notes")
            });
        }

        return rows;
    }

    private static void SavePriceObservations(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<PriceObservation> rows)
    {
        foreach (var row in rows)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO price_observation (
                    id, product_id, retailer, store_location, country, state, county, city,
                    shelf_price, loyalty_or_sale_price, tax_amount, deposit_or_fee,
                    estimated_checkout_price, actual_checkout_price, observed_on, sale_starts_on,
                    sale_ends_on, source_name, source_type, confidence, price_kind, review_by_date,
                    tax_category, tax_collection, excise_embedded, notes)
                VALUES (
                    $id, $product, $retailer, $store, $country, $state, $county, $city, $shelf,
                    $sale, $tax, $deposit, $estimated, $actual, $observed, $starts, $ends, $source,
                    $sourceType, $confidence, $kind, $review, $taxCat, $collection, $excise, $notes);
                """);
            command.Parameters.AddWithValue("$id", SqliteValues.ToText(row.Id));
            command.Parameters.AddWithValue("$product", SqliteValues.ToText(row.ProductId));
            command.Parameters.AddWithValue("$retailer", SqliteValues.ToNullable(row.Retailer));
            command.Parameters.AddWithValue("$store", SqliteValues.ToNullable(row.StoreLocation));
            command.Parameters.AddWithValue("$country", row.Locality.Country);
            command.Parameters.AddWithValue("$state", SqliteValues.ToNullable(row.Locality.State));
            command.Parameters.AddWithValue("$county", SqliteValues.ToNullable(row.Locality.County));
            command.Parameters.AddWithValue("$city", SqliteValues.ToNullable(row.Locality.City));
            command.Parameters.AddWithValue("$shelf", SqliteValues.ToText(row.ShelfPrice));
            command.Parameters.AddWithValue("$sale", SqliteValues.ToNullable(row.LoyaltyOrSalePrice));
            command.Parameters.AddWithValue("$tax", SqliteValues.ToNullable(row.TaxAmount));
            command.Parameters.AddWithValue("$deposit", SqliteValues.ToNullable(row.DepositOrFee));
            command.Parameters.AddWithValue("$estimated", SqliteValues.ToNullable(row.EstimatedCheckoutPrice));
            command.Parameters.AddWithValue("$actual", SqliteValues.ToNullable(row.ActualCheckoutPrice));
            command.Parameters.AddWithValue("$observed", SqliteValues.ToText(row.ObservedOn));
            command.Parameters.AddWithValue("$starts", SqliteValues.ToNullable(row.SaleStartsOn));
            command.Parameters.AddWithValue("$ends", SqliteValues.ToNullable(row.SaleEndsOn));
            command.Parameters.AddWithValue("$source", row.SourceName);
            command.Parameters.AddWithValue("$sourceType", (int)row.SourceType);
            command.Parameters.AddWithValue("$confidence", (int)row.Confidence);
            command.Parameters.AddWithValue("$kind", (int)row.PriceKind);
            command.Parameters.AddWithValue("$review", SqliteValues.ToNullable(row.ReviewByDate));
            command.Parameters.AddWithValue("$taxCat", (int)row.TaxCategory);
            command.Parameters.AddWithValue("$collection", (int)row.TaxCollection);
            command.Parameters.AddWithValue("$excise", row.ExciseEmbeddedInShelfPrice ? 1 : 0);
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(row.Notes));
            command.ExecuteNonQuery();
        }
    }

    private static List<SalesTaxRule> LoadSalesTaxRules(SqliteConnection connection)
    {
        var rows = new List<SalesTaxRule>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM sales_tax_rule;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(new SalesTaxRule
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                State = reader.GetString(reader.GetOrdinal("state")),
                County = SqliteValues.GetNullableString(reader, "county"),
                City = SqliteValues.GetNullableString(reader, "city"),
                Category = SqliteValues.GetEnum<ProductTaxCategory>(reader, "category"),
                Rate = SqliteValues.GetDecimal(reader, "rate"),
                EffectiveDate = SqliteValues.GetDate(reader, "effective_date"),
                ReviewByDate = SqliteValues.GetNullableDate(reader, "review_by_date"),
                OfficialSource = reader.GetString(reader.GetOrdinal("official_source")),
                RuleSetVersion = reader.GetString(reader.GetOrdinal("rule_set_version")),
                Collection = SqliteValues.GetEnum<TaxCollectionMethod>(reader, "collection"),
                ExciseAlreadyInShelfPrice = SqliteValues.GetBool(reader, "excise_already_in_shelf")
            });
        }

        return rows;
    }

    private static void SaveSalesTaxRules(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<SalesTaxRule> rows)
    {
        foreach (var row in rows)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO sales_tax_rule (
                    id, state, county, city, category, rate, effective_date, review_by_date,
                    official_source, rule_set_version, collection, excise_already_in_shelf)
                VALUES (
                    $id, $state, $county, $city, $category, $rate, $effective, $review,
                    $source, $version, $collection, $excise);
                """);
            command.Parameters.AddWithValue("$id", SqliteValues.ToText(row.Id));
            command.Parameters.AddWithValue("$state", row.State);
            command.Parameters.AddWithValue("$county", SqliteValues.ToNullable(row.County));
            command.Parameters.AddWithValue("$city", SqliteValues.ToNullable(row.City));
            command.Parameters.AddWithValue("$category", (int)row.Category);
            command.Parameters.AddWithValue("$rate", SqliteValues.ToText(row.Rate));
            command.Parameters.AddWithValue("$effective", SqliteValues.ToText(row.EffectiveDate));
            command.Parameters.AddWithValue("$review", SqliteValues.ToNullable(row.ReviewByDate));
            command.Parameters.AddWithValue("$source", row.OfficialSource);
            command.Parameters.AddWithValue("$version", row.RuleSetVersion);
            command.Parameters.AddWithValue("$collection", (int)row.Collection);
            command.Parameters.AddWithValue("$excise", row.ExciseAlreadyInShelfPrice ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private static List<ExchangeRateQuote> LoadExchangeRates(SqliteConnection connection)
    {
        var rows = new List<ExchangeRateQuote>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM exchange_rate_quote ORDER BY timestamp_utc DESC;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(new ExchangeRateQuote
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                SourceCurrency = reader.GetString(reader.GetOrdinal("source_currency")),
                DestinationCurrency = reader.GetString(reader.GetOrdinal("destination_currency")),
                Rate = SqliteValues.GetDecimal(reader, "rate"),
                Timestamp = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("timestamp_utc")), CultureInfo.InvariantCulture),
                SourceName = reader.GetString(reader.GetOrdinal("source_name")),
                MidMarketRate = SqliteValues.GetNullableDecimal(reader, "mid_market_rate"),
                ProviderRate = SqliteValues.GetNullableDecimal(reader, "provider_rate"),
                ActualRateReceived = SqliteValues.GetNullableDecimal(reader, "actual_rate_received"),
                Spread = SqliteValues.GetNullableDecimal(reader, "spread"),
                ProviderFee = SqliteValues.GetMoney(reader, "provider_fee")
            });
        }

        return rows;
    }

    private static void SaveExchangeRates(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<ExchangeRateQuote> rows)
    {
        foreach (var row in rows)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO exchange_rate_quote (
                    id, source_currency, destination_currency, rate, timestamp_utc, source_name,
                    mid_market_rate, provider_rate, actual_rate_received, spread, provider_fee)
                VALUES (
                    $id, $from, $to, $rate, $when, $source, $mid, $provider, $actual, $spread, $fee);
                """);
            command.Parameters.AddWithValue("$id", SqliteValues.ToText(row.Id));
            command.Parameters.AddWithValue("$from", row.SourceCurrency);
            command.Parameters.AddWithValue("$to", row.DestinationCurrency);
            command.Parameters.AddWithValue("$rate", SqliteValues.ToText(row.Rate));
            command.Parameters.AddWithValue("$when", row.Timestamp.ToString("O"));
            command.Parameters.AddWithValue("$source", row.SourceName);
            command.Parameters.AddWithValue("$mid", SqliteValues.ToNullable(row.MidMarketRate));
            command.Parameters.AddWithValue("$provider", SqliteValues.ToNullable(row.ProviderRate));
            command.Parameters.AddWithValue("$actual", SqliteValues.ToNullable(row.ActualRateReceived));
            command.Parameters.AddWithValue("$spread", SqliteValues.ToNullable(row.Spread));
            command.Parameters.AddWithValue("$fee", SqliteValues.ToText(row.ProviderFee));
            command.ExecuteNonQuery();
        }
    }

    private static List<InternationalTransfer> LoadInternationalTransfers(SqliteConnection connection)
    {
        var rows = new List<InternationalTransfer>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM international_transfer ORDER BY transfer_date DESC;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(new InternationalTransfer
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                TransferDate = SqliteValues.GetDate(reader, "transfer_date"),
                Sender = reader.GetString(reader.GetOrdinal("sender")),
                Recipient = reader.GetString(reader.GetOrdinal("recipient")),
                SendingCountry = reader.GetString(reader.GetOrdinal("sending_country")),
                ReceivingCountry = reader.GetString(reader.GetOrdinal("receiving_country")),
                SendingAccountOwnerMemberId = SqliteValues.GetNullableGuid(reader, "sending_owner_member_id"),
                ReceivingAccountOwnerMemberId = SqliteValues.GetNullableGuid(reader, "receiving_owner_member_id"),
                Relationship = SqliteValues.GetEnum<TransferRelationship>(reader, "relationship"),
                SourceCurrency = reader.GetString(reader.GetOrdinal("source_currency")),
                DestinationCurrency = reader.GetString(reader.GetOrdinal("destination_currency")),
                AmountSent = SqliteValues.GetMoney(reader, "amount_sent"),
                ExchangeRate = SqliteValues.GetDecimal(reader, "exchange_rate"),
                AmountReceived = SqliteValues.GetMoney(reader, "amount_received"),
                Provider = SqliteValues.GetNullableString(reader, "provider"),
                ProviderFee = SqliteValues.GetMoney(reader, "provider_fee"),
                SendingBankFee = SqliteValues.GetMoney(reader, "sending_bank_fee"),
                IntermediaryBankFee = SqliteValues.GetMoney(reader, "intermediary_bank_fee"),
                RecipientFee = SqliteValues.GetMoney(reader, "recipient_fee"),
                ExchangeRateSpread = SqliteValues.GetNullableDecimal(reader, "exchange_rate_spread"),
                Purpose = SqliteValues.GetEnum<TransferPurpose>(reader, "purpose"),
                CustomPurpose = SqliteValues.GetNullableString(reader, "custom_purpose"),
                TaxClassification = SqliteValues.GetEnum<TransferTaxClassification>(reader, "tax_classification"),
                ReportingClassification = SqliteValues.GetNullableString(reader, "reporting_classification"),
                LinkedTransferId = SqliteValues.GetNullableGuid(reader, "linked_transfer_id"),
                SupportingDocumentId = SqliteValues.GetNullableGuid(reader, "supporting_document_id"),
                HierarchyTier = SqliteValues.GetNullableEnum<NeedTier>(reader, "hierarchy_tier"),
                Frequency = SqliteValues.GetEnum<Frequency>(reader, "frequency"),
                IsFixedDestinationAmount = SqliteValues.GetBool(reader, "is_fixed_destination"),
                ExchangeRateSafetyMargin = SqliteValues.GetNullableDecimal(reader, "exchange_rate_safety_margin"),
                DueDate = SqliteValues.GetNullableDate(reader, "due_date"),
                ReviewStatus = SqliteValues.GetEnum<ClassificationReviewStatus>(reader, "review_status"),
                ClassificationCorrectedOn = SqliteValues.GetNullableDate(reader, "classification_corrected_on"),
                ClassificationCorrectionNote = SqliteValues.GetNullableString(reader, "classification_correction_note"),
                OriginalClassification = SqliteValues.GetNullableEnum<TransferTaxClassification>(reader, "original_classification"),
                OriginalExchangeRate = SqliteValues.GetNullableDecimal(reader, "original_exchange_rate"),
                Notes = SqliteValues.GetNullableString(reader, "notes")
            });
        }

        return rows;
    }

    private static void SaveInternationalTransfers(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<InternationalTransfer> rows)
    {
        foreach (var row in rows)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO international_transfer (
                    id, transfer_date, sender, recipient, sending_country, receiving_country,
                    sending_owner_member_id, receiving_owner_member_id, relationship,
                    source_currency, destination_currency, amount_sent, exchange_rate, amount_received,
                    provider, provider_fee, sending_bank_fee, intermediary_bank_fee, recipient_fee,
                    exchange_rate_spread, purpose, custom_purpose, tax_classification,
                    reporting_classification, linked_transfer_id, supporting_document_id,
                    hierarchy_tier, frequency, is_fixed_destination, exchange_rate_safety_margin,
                    due_date, review_status, classification_corrected_on, classification_correction_note,
                    original_classification, original_exchange_rate, notes)
                VALUES (
                    $id, $date, $sender, $recipient, $fromCountry, $toCountry, $fromOwner, $toOwner,
                    $relationship, $fromCcy, $toCcy, $sent, $rate, $received, $provider, $pfee,
                    $sfee, $ifee, $rfee, $spread, $purpose, $custom, $tax, $reporting, $linked,
                    $doc, $tier, $freq, $fixed, $margin, $due, $review, $corrected, $correction,
                    $original, $originalRate, $notes);
                """);
            command.Parameters.AddWithValue("$id", SqliteValues.ToText(row.Id));
            command.Parameters.AddWithValue("$date", SqliteValues.ToText(row.TransferDate));
            command.Parameters.AddWithValue("$sender", row.Sender);
            command.Parameters.AddWithValue("$recipient", row.Recipient);
            command.Parameters.AddWithValue("$fromCountry", row.SendingCountry);
            command.Parameters.AddWithValue("$toCountry", row.ReceivingCountry);
            command.Parameters.AddWithValue("$fromOwner", SqliteValues.ToNullable(row.SendingAccountOwnerMemberId));
            command.Parameters.AddWithValue("$toOwner", SqliteValues.ToNullable(row.ReceivingAccountOwnerMemberId));
            command.Parameters.AddWithValue("$relationship", (int)row.Relationship);
            command.Parameters.AddWithValue("$fromCcy", row.SourceCurrency);
            command.Parameters.AddWithValue("$toCcy", row.DestinationCurrency);
            command.Parameters.AddWithValue("$sent", SqliteValues.ToText(row.AmountSent));
            command.Parameters.AddWithValue("$rate", SqliteValues.ToText(row.ExchangeRate));
            command.Parameters.AddWithValue("$received", SqliteValues.ToText(row.AmountReceived));
            command.Parameters.AddWithValue("$provider", SqliteValues.ToNullable(row.Provider));
            command.Parameters.AddWithValue("$pfee", SqliteValues.ToText(row.ProviderFee));
            command.Parameters.AddWithValue("$sfee", SqliteValues.ToText(row.SendingBankFee));
            command.Parameters.AddWithValue("$ifee", SqliteValues.ToText(row.IntermediaryBankFee));
            command.Parameters.AddWithValue("$rfee", SqliteValues.ToText(row.RecipientFee));
            command.Parameters.AddWithValue("$spread", SqliteValues.ToNullable(row.ExchangeRateSpread));
            command.Parameters.AddWithValue("$purpose", (int)row.Purpose);
            command.Parameters.AddWithValue("$custom", SqliteValues.ToNullable(row.CustomPurpose));
            command.Parameters.AddWithValue("$tax", (int)row.TaxClassification);
            command.Parameters.AddWithValue("$reporting", SqliteValues.ToNullable(row.ReportingClassification));
            command.Parameters.AddWithValue("$linked", SqliteValues.ToNullable(row.LinkedTransferId));
            command.Parameters.AddWithValue("$doc", SqliteValues.ToNullable(row.SupportingDocumentId));
            command.Parameters.AddWithValue("$tier", SqliteValues.ToNullable((int?)row.HierarchyTier));
            command.Parameters.AddWithValue("$freq", (int)row.Frequency);
            command.Parameters.AddWithValue("$fixed", row.IsFixedDestinationAmount ? 1 : 0);
            command.Parameters.AddWithValue("$margin", SqliteValues.ToNullable(row.ExchangeRateSafetyMargin));
            command.Parameters.AddWithValue("$due", SqliteValues.ToNullable(row.DueDate));
            command.Parameters.AddWithValue("$review", (int)row.ReviewStatus);
            command.Parameters.AddWithValue("$corrected", SqliteValues.ToNullable(row.ClassificationCorrectedOn));
            command.Parameters.AddWithValue("$correction", SqliteValues.ToNullable(row.ClassificationCorrectionNote));
            command.Parameters.AddWithValue("$original", SqliteValues.ToNullable((int?)row.OriginalClassification));
            command.Parameters.AddWithValue("$originalRate", SqliteValues.ToNullable(row.OriginalExchangeRate));
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(row.Notes));
            command.ExecuteNonQuery();
        }
    }

    private static List<RecurringSupportCommitment> LoadSupportCommitments(SqliteConnection connection)
    {
        var rows = new List<RecurringSupportCommitment>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM support_commitment;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(new RecurringSupportCommitment
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Frequency = SqliteValues.GetEnum<Frequency>(reader, "frequency"),
                FixedUsdToSend = SqliteValues.GetNullableMoney(reader, "fixed_usd_to_send"),
                FixedZarToReceive = SqliteValues.GetNullableMoney(reader, "fixed_zar_to_receive"),
                ExpectedRateUsdToZar = SqliteValues.GetDecimal(reader, "expected_rate_usd_zar"),
                ExpectedFees = SqliteValues.GetMoney(reader, "expected_fees"),
                SafetyMargin = SqliteValues.GetDecimal(reader, "safety_margin"),
                DueDate = SqliteValues.GetNullableDate(reader, "due_date"),
                HierarchyTier = SqliteValues.GetNullableEnum<NeedTier>(reader, "hierarchy_tier")
            });
        }

        return rows;
    }

    private static void SaveSupportCommitments(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<RecurringSupportCommitment> rows)
    {
        foreach (var row in rows)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO support_commitment (
                    id, name, frequency, fixed_usd_to_send, fixed_zar_to_receive,
                    expected_rate_usd_zar, expected_fees, safety_margin, due_date, hierarchy_tier)
                VALUES ($id, $name, $freq, $usd, $zar, $rate, $fees, $margin, $due, $tier);
                """);
            command.Parameters.AddWithValue("$id", SqliteValues.ToText(row.Id));
            command.Parameters.AddWithValue("$name", row.Name);
            command.Parameters.AddWithValue("$freq", (int)row.Frequency);
            command.Parameters.AddWithValue("$usd", SqliteValues.ToNullable(row.FixedUsdToSend));
            command.Parameters.AddWithValue("$zar", SqliteValues.ToNullable(row.FixedZarToReceive));
            command.Parameters.AddWithValue("$rate", SqliteValues.ToText(row.ExpectedRateUsdToZar));
            command.Parameters.AddWithValue("$fees", SqliteValues.ToText(row.ExpectedFees));
            command.Parameters.AddWithValue("$margin", SqliteValues.ToText(row.SafetyMargin));
            command.Parameters.AddWithValue("$due", SqliteValues.ToNullable(row.DueDate));
            command.Parameters.AddWithValue("$tier", SqliteValues.ToNullable((int?)row.HierarchyTier));
            command.ExecuteNonQuery();
        }
    }

    private static List<ForeignAccount> LoadForeignAccounts(SqliteConnection connection)
    {
        var rows = new List<ForeignAccount>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM foreign_account;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(new ForeignAccount
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                OwnerMemberId = SqliteValues.GetNullableGuid(reader, "owner_member_id"),
                Institution = reader.GetString(reader.GetOrdinal("institution")),
                Country = reader.GetString(reader.GetOrdinal("country")),
                Nickname = reader.GetString(reader.GetOrdinal("nickname")),
                Currency = reader.GetString(reader.GetOrdinal("currency")),
                MaximumCalendarYearBalance = SqliteValues.GetNullableMoney(reader, "maximum_calendar_year_balance"),
                YearEndBalance = SqliteValues.GetNullableMoney(reader, "year_end_balance"),
                ReportingExchangeRate = SqliteValues.GetNullableDecimal(reader, "reporting_exchange_rate"),
                UsdEquivalent = SqliteValues.GetNullableMoney(reader, "usd_equivalent"),
                HasFinancialInterest = SqliteValues.GetBool(reader, "has_financial_interest"),
                HasSignatureAuthority = SqliteValues.GetBool(reader, "has_signature_authority"),
                OpenedOn = SqliteValues.GetNullableDate(reader, "opened_on"),
                ClosedOn = SqliteValues.GetNullableDate(reader, "closed_on"),
                ReviewStatus = SqliteValues.GetEnum<ClassificationReviewStatus>(reader, "review_status")
            });
        }

        return rows;
    }

    private static void SaveForeignAccounts(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<ForeignAccount> rows)
    {
        foreach (var row in rows)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO foreign_account (
                    id, owner_member_id, institution, country, nickname, currency,
                    maximum_calendar_year_balance, year_end_balance, reporting_exchange_rate,
                    usd_equivalent, has_financial_interest, has_signature_authority,
                    opened_on, closed_on, review_status)
                VALUES (
                    $id, $owner, $institution, $country, $nickname, $currency, $max, $yearEnd,
                    $rate, $usd, $interest, $sign, $opened, $closed, $review);
                """);
            command.Parameters.AddWithValue("$id", SqliteValues.ToText(row.Id));
            command.Parameters.AddWithValue("$owner", SqliteValues.ToNullable(row.OwnerMemberId));
            command.Parameters.AddWithValue("$institution", row.Institution);
            command.Parameters.AddWithValue("$country", row.Country);
            command.Parameters.AddWithValue("$nickname", row.Nickname);
            command.Parameters.AddWithValue("$currency", row.Currency);
            command.Parameters.AddWithValue("$max", SqliteValues.ToNullable(row.MaximumCalendarYearBalance));
            command.Parameters.AddWithValue("$yearEnd", SqliteValues.ToNullable(row.YearEndBalance));
            command.Parameters.AddWithValue("$rate", SqliteValues.ToNullable(row.ReportingExchangeRate));
            command.Parameters.AddWithValue("$usd", SqliteValues.ToNullable(row.UsdEquivalent));
            command.Parameters.AddWithValue("$interest", row.HasFinancialInterest ? 1 : 0);
            command.Parameters.AddWithValue("$sign", row.HasSignatureAuthority ? 1 : 0);
            command.Parameters.AddWithValue("$opened", SqliteValues.ToNullable(row.OpenedOn));
            command.Parameters.AddWithValue("$closed", SqliteValues.ToNullable(row.ClosedOn));
            command.Parameters.AddWithValue("$review", (int)row.ReviewStatus);
            command.ExecuteNonQuery();
        }
    }

    private static List<SupportingDocumentRef> LoadSupportingDocuments(SqliteConnection connection)
    {
        var rows = new List<SupportingDocumentRef>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM supporting_document;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(new SupportingDocumentRef
            {
                Id = SqliteValues.GetGuid(reader, "id"),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Kind = reader.GetString(reader.GetOrdinal("kind")),
                DocumentDate = SqliteValues.GetNullableDate(reader, "document_date"),
                EncryptedStorageKey = SqliteValues.GetNullableString(reader, "encrypted_storage_key"),
                Notes = SqliteValues.GetNullableString(reader, "notes")
            });
        }

        return rows;
    }

    private static void SaveSupportingDocuments(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<SupportingDocumentRef> rows)
    {
        foreach (var row in rows)
        {
            using var command = Command(connection, transaction, """
                INSERT INTO supporting_document (id, title, kind, document_date, encrypted_storage_key, notes)
                VALUES ($id, $title, $kind, $date, $key, $notes);
                """);
            command.Parameters.AddWithValue("$id", SqliteValues.ToText(row.Id));
            command.Parameters.AddWithValue("$title", row.Title);
            command.Parameters.AddWithValue("$kind", row.Kind);
            command.Parameters.AddWithValue("$date", SqliteValues.ToNullable(row.DocumentDate));
            command.Parameters.AddWithValue("$key", SqliteValues.ToNullable(row.EncryptedStorageKey));
            command.Parameters.AddWithValue("$notes", SqliteValues.ToNullable(row.Notes));
            command.ExecuteNonQuery();
        }
    }

    private static string? LoadGuidancePackVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT imported_version FROM guidance_pack_state WHERE id = 1;";
        return command.ExecuteScalar() as string;
    }

    private static void SaveGuidancePackVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? version)
    {
        using var command = Command(connection, transaction, """
            INSERT INTO guidance_pack_state (id, imported_version, imported_on)
            VALUES (1, $version, $when);
            """);
        command.Parameters.AddWithValue("$version", SqliteValues.ToNullable(version));
        command.Parameters.AddWithValue("$when", version is null ? DBNull.Value : SqliteValues.ToText(DateOnly.FromDateTime(DateTime.Today)));
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> LoadOutstandingQuestions(SqliteConnection connection)
    {
        var questions = new List<string>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT message FROM outstanding_question ORDER BY sort_order, message;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var message = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(message))
            {
                questions.Add(message);
            }
        }

        return questions;
    }

    private static void SaveOutstandingQuestions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<string> questions)
    {
        for (var index = 0; index < questions.Count; index++)
        {
            var message = questions[index].Trim();
            if (message.Length == 0)
            {
                continue;
            }

            using var command = Command(connection, transaction, """
                INSERT INTO outstanding_question (id, message, sort_order)
                VALUES ($id, $message, $order);
                """);

            command.Parameters.AddWithValue("$id", SqliteValues.ToText(Guid.NewGuid()));
            command.Parameters.AddWithValue("$message", message);
            command.Parameters.AddWithValue("$order", index);
            command.ExecuteNonQuery();
        }
    }

    #endregion

    public IReadOnlyList<AuditRecord> ReadAuditTrail(int maximum = 250)
    {
        var take = Math.Clamp(maximum, 1, 1000);
        return _database.Read(connection =>
        {
            var records = new List<AuditRecord>();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT occurred_utc, event_name, detail, operation, record_type, record_id, user_note
                FROM audit_log
                ORDER BY id DESC
                LIMIT $take;
                """;
            command.Parameters.AddWithValue("$take", take);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(new AuditRecord(
                    DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }

            return records;
        });
    }

    private static SqliteCommand Command(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
