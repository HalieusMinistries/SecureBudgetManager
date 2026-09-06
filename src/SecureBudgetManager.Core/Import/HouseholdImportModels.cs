using SecureBudgetManager.Core.Benefits;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.CashFlow;
using SecureBudgetManager.Core.Debt;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.International;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Planning;
using SecureBudgetManager.Core.Savings;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.Core.Import;

public sealed record ImportPreview(
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Updated,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<string> Conflicts)
{
    public bool HasMaterialConflicts => Conflicts.Count > 0;

    public string Summary =>
        $"{Created.Count} created, {Updated.Count} updated, {Unchanged.Count} unchanged, {Conflicts.Count} conflict(s).";
}

public sealed record ImportMergeResult(BudgetDocument Document, ImportPreview Preview);

public sealed record ImportVerificationCheck(string Name, bool Passed, string Detail);

public sealed record ImportVerificationReport(
    IReadOnlyList<ImportVerificationCheck> Checks,
    ImportPreview Preview)
{
    public bool AllPassed => Checks.All(check => check.Passed);
}

public sealed record ImportVerificationExpectations
{
    public string? SharedAccountNameContains { get; init; }

    public decimal? ExpectedSharedOpeningBalance { get; init; }

    public DateOnly? OpeningBalanceAsOf { get; init; }

    public decimal? ExpectedPayslipGross { get; init; }

    public decimal? ExpectedPayslipTax { get; init; }

    public decimal? ExpectedPayslipNetWages { get; init; }

    public decimal? ExpectedPayslipReimbursement { get; init; }

    public decimal? ExpectedPayslipDeposit { get; init; }

    public DateOnly? HistoricalPayslipDate { get; init; }

    public decimal? ExpectedWeeklyBenefitTotal { get; init; }

    public decimal? FortnightlyInsuranceAmount { get; init; }

    public DateOnly? UnconfirmedPayAnchor { get; init; }

    public DateOnly? DoNotGeneratePayAfter { get; init; }
}

/// <summary>
/// A portable household snapshot used only for validated import. It is not an application default
/// and must not be compiled into the executable.
/// </summary>
public sealed record HouseholdImportPayload
{
    public int SchemaVersion { get; init; } = 1;

    public required string HouseholdName { get; init; }

    public CostLocality Locality { get; init; } = CostLocality.UnitedStates;

    public HouseholdPreferences Preferences { get; init; } = new();

    public AllocationRules Rules { get; init; } = new();

    public IReadOnlyList<HouseholdMember> Members { get; init; } = [];

    public IReadOnlyList<BankAccount> Accounts { get; init; } = [];

    public IReadOnlyList<ImportIncomeDto> Income { get; init; } = [];

    public IReadOnlyList<Payslip> Payslips { get; init; } = [];

    public IReadOnlyList<PayrollProfile> Payroll { get; init; } = [];

    public IReadOnlyList<BenefitPlan> Benefits { get; init; } = [];

    public IReadOnlyList<ExpenseItem> Expenses { get; init; } = [];

    public IReadOnlyList<DebtAccount> Debts { get; init; } = [];

    public IReadOnlyList<SavingsFund> Funds { get; init; } = [];

    public IReadOnlyList<GroceryPlan> GroceryPlans { get; init; } = [];

    public IReadOnlyList<ForeignAccount> ForeignAccounts { get; init; } = [];

    public IReadOnlyList<Scenario> Scenarios { get; init; } = [];

    public IReadOnlyList<string> OutstandingQuestions { get; init; } = [];

    public ImportVerificationExpectations Verification { get; init; } = new();

    public BudgetDocument ToDocument()
    {
        var document = new BudgetDocument
        {
            HouseholdName = HouseholdName.Trim(),
            Locality = Locality,
            Preferences = Preferences,
            Rules = Rules,
            Members = Members,
            Accounts = Accounts,
            IncomeSources = Income.Select(item => item.ToSource()).ToList(),
            Payslips = Payslips,
            PayrollProfiles = Payroll,
            Benefits = Benefits,
            Expenses = Expenses,
            Debts = Debts,
            Funds = Funds,
            GroceryPlans = GroceryPlans,
            ForeignAccounts = ForeignAccounts,
            Scenarios = Scenarios,
            OutstandingQuestions = OutstandingQuestions
                .Where(question => !string.IsNullOrWhiteSpace(question))
                .Select(question => question.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };

        document.Validate();
        return document;
    }
}

public sealed record ImportIncomeDto
{
    public required string Kind { get; init; }

    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required Guid MemberId { get; init; }

    public required Frequency PayFrequency { get; init; }

    public required DateOnly AnchorPayDate { get; init; }

    public bool IsTaxable { get; init; } = true;

    public bool IsActive { get; init; } = true;

    public bool PayScheduleConfirmed { get; init; } = true;

    public string? Notes { get; init; }

    public decimal? HourlyRate { get; init; }

    public decimal? HoursConservative { get; init; }

    public decimal? HoursNormal { get; init; }

    public decimal? HoursOptimistic { get; init; }

    public decimal? OvertimeConservative { get; init; }

    public decimal? OvertimeNormal { get; init; }

    public decimal? OvertimeOptimistic { get; init; }

    public decimal? OvertimeMultiplier { get; init; }

    public decimal? AmountConservative { get; init; }

    public decimal? AmountNormal { get; init; }

    public decimal? AmountOptimistic { get; init; }

    public decimal? MilesPerPeriod { get; init; }

    public decimal? RatePerMile { get; init; }

    public IncomeSource ToSource() => Kind.Trim().ToLowerInvariant() switch
    {
        "hourly" => new HourlyIncome
        {
            Id = Id,
            Name = Name,
            MemberId = MemberId,
            PayFrequency = PayFrequency,
            AnchorPayDate = AnchorPayDate,
            IsTaxable = IsTaxable,
            IsActive = IsActive,
            PayScheduleConfirmed = PayScheduleConfirmed,
            Notes = Notes,
            HourlyRate = new Money(HourlyRate ?? 0m),
            WeeklyHours = new VariableHours(
                HoursConservative ?? 0m,
                HoursNormal ?? HoursConservative ?? 0m,
                HoursOptimistic ?? HoursNormal ?? HoursConservative ?? 0m),
            WeeklyOvertimeHours = new VariableHours(
                OvertimeConservative ?? 0m,
                OvertimeNormal ?? OvertimeConservative ?? 0m,
                OvertimeOptimistic ?? OvertimeNormal ?? OvertimeConservative ?? 0m),
            OvertimeMultiplier = OvertimeMultiplier ?? 1.5m
        },
        "variable" => new VariableIncome
        {
            Id = Id,
            Name = Name,
            MemberId = MemberId,
            PayFrequency = PayFrequency,
            AnchorPayDate = AnchorPayDate,
            IsTaxable = IsTaxable,
            IsActive = IsActive,
            PayScheduleConfirmed = PayScheduleConfirmed,
            Notes = Notes,
            AmountPerPeriod = new VariableHours(
                AmountConservative ?? 0m,
                AmountNormal ?? AmountConservative ?? 0m,
                AmountOptimistic ?? AmountNormal ?? AmountConservative ?? 0m)
        },
        "mileage" => new MileageReimbursement
        {
            Id = Id,
            Name = Name,
            MemberId = MemberId,
            PayFrequency = PayFrequency,
            AnchorPayDate = AnchorPayDate,
            IsTaxable = false,
            IsActive = IsActive,
            PayScheduleConfirmed = PayScheduleConfirmed,
            Notes = Notes,
            MilesPerPeriod = MilesPerPeriod ?? 0m,
            RatePerMile = new Money(RatePerMile ?? 0m)
        },
        _ => throw new ArgumentException($"Unknown income kind \"{Kind}\".")
    };
}

public static class HouseholdImportSerializer
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters =
        {
            new MoneyJsonConverter(),
            new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)
        }
    };

    public static HouseholdImportPayload Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        HouseholdImportGuard.RefuseSensitiveIdentifiers(json);

        var payload = System.Text.Json.JsonSerializer.Deserialize<HouseholdImportPayload>(json, Options)
            ?? throw new ArgumentException("The import file did not contain a household payload.");

        if (payload.SchemaVersion != 1)
        {
            throw new ArgumentException($"Unsupported household import schema {payload.SchemaVersion}.");
        }

        return payload;
    }
}

public static class HouseholdImportGuard
{
    public static void RefuseSensitiveIdentifiers(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (Contains(text, "social security")
            || Contains(text, "\"ssn\"")
            || Contains(text, "itin")
            || Contains(text, "passport")
            || Contains(text, "i-94")
            || Contains(text, "alien number")
            || Contains(text, "a-number")
            || System.Text.RegularExpressions.Regex.IsMatch(
                text,
                @"\b\d{3}-\d{2}-\d{4}\b"))
        {
            throw new ArgumentException(
                "The import file appears to contain a government identifier and was refused.");
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d{9,17}\b")
            && Contains(text, "account number"))
        {
            throw new ArgumentException(
                "The import file appears to contain an account number and was refused.");
        }
    }

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);
}

public sealed class MoneyJsonConverter : System.Text.Json.Serialization.JsonConverter<Money>
{
    public override Money Read(
        ref System.Text.Json.Utf8JsonReader reader,
        Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.Number)
        {
            return new Money(reader.GetDecimal());
        }

        if (reader.TokenType == System.Text.Json.JsonTokenType.StartObject)
        {
            using var document = System.Text.Json.JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("amount", out var amount))
            {
                return new Money(amount.GetDecimal());
            }
        }

        throw new System.Text.Json.JsonException("A money value must be a number.");
    }

    public override void Write(
        System.Text.Json.Utf8JsonWriter writer,
        Money value,
        System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Amount);
}

public static class HouseholdImportVerifier
{
    public static ImportVerificationReport Verify(
        BudgetDocument document,
        ImportPreview preview,
        ImportVerificationExpectations expectations,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(expectations);

        var checks = new List<ImportVerificationCheck>
        {
            Check(
                "No duplicate household members",
                document.Members.Select(member => Normalize(member.Name)).Distinct().Count()
                == document.Members.Count,
                $"{document.Members.Count} member(s) after merge."),
            Check(
                "No dependant discretionary eligibility",
                document.Members.Where(member => member.IsDependant)
                    .All(member => !member.IsDiscretionaryEligible && member.PersonalAllowance.IsZero),
                "Dependants are included in household size without a personal envelope."),
            Check(
                "Earners keep separate discretionary eligibility",
                document.Members.Count(member => !member.IsDependant && member.IsDiscretionaryEligible) >= 2
                || document.Members.Count(member => !member.IsDependant) < 2,
                "Adult earners remain separately eligible when two adults are present."),
            Check(
                "Mileage reimbursement is non-taxable",
                document.IncomeSources.OfType<MileageReimbursement>().All(source => !source.IsTaxable),
                "Mileage sources are stored separately from taxable wages."),
            Check(
                "Cigarettes and beer are outside groceries and protected essentials",
                document.Expenses.Where(IsTobaccoOrAlcohol).All(expense =>
                    expense.Necessity == ExpenseNecessity.Optional
                    && !string.Equals(
                        expense.Category.Name,
                        ExpenseCategory.Groceries.Name,
                        StringComparison.OrdinalIgnoreCase)
                    && !document.Hierarchy.IsProtected(expense.Category.Name, expense.Necessity)),
                "Tobacco and alcohol use discretionary categories."),
            Check(
                "Unknown due dates do not create calendar transactions",
                document.Expenses.Where(expense => expense.DueDateUnknown)
                    .All(expense => !expense.DueDates(today, today.AddYears(1)).Any()),
                "Due-date-unknown expenses generate no occurrences."),
            Check(
                "Unconfigured savings targets are not stored as money",
                document.Funds.All(fund =>
                    fund.TargetAmount is null
                    || fund.TargetAmount > Money.Zero
                    || fund.CurrentBalance >= Money.Zero),
                "Funds may have a zero balance; empty targets stay unconfigured."),
            Check(
                "Fortnightly conversions use 26 payments a year",
                Frequency.Fortnightly.PaymentsPerYear() == 26m
                && FrequencyConverter.ToAnnual(new Money(53m), Frequency.Fortnightly).Round().Amount == 1378m
                && FrequencyConverter.ToWeekly(new Money(53m), Frequency.Fortnightly).Round().Amount == 26.50m
                && FrequencyConverter.ToMonthly(new Money(53m), Frequency.Fortnightly).Round().Amount == 114.83m,
                "Weekly 26.50, average monthly 114.83, annual 1,378.00 from 53 fortnightly.")
        };

        if (expectations.ExpectedPayslipDeposit is { } deposit
            && expectations.HistoricalPayslipDate is { } payDate)
        {
            var slip = document.Payslips.FirstOrDefault(item => item.PayDate == payDate);
            var expectedGross = new Money(expectations.ExpectedPayslipGross ?? 0m);
            var expectedTax = new Money(expectations.ExpectedPayslipTax ?? 0m);
            var expectedNet = new Money(expectations.ExpectedPayslipNetWages ?? 0m);
            var expectedReimbursement = new Money(expectations.ExpectedPayslipReimbursement ?? 0m);
            var expectedDeposit = new Money(deposit);

            checks.Add(Check(
                "Historical payslip reconciles",
                slip is not null
                && slip.GrossPay == expectedGross
                && slip.TotalWithholding == expectedTax
                && (slip.GrossPay - slip.TotalWithholding).Round() == expectedNet
                && slip.NonTaxableReimbursements == expectedReimbursement
                && slip.NetPay.Round() == expectedDeposit
                && slip.HoursWorked == 7.75m,
                slip is null
                    ? "No matching payslip was stored."
                    : $"Gross {slip.GrossPay.ToDisplayString()} − tax {slip.TotalWithholding.ToDisplayString()} " +
                      $"+ reimbursement {slip.NonTaxableReimbursements.ToDisplayString()} = {slip.NetPay.Round().ToDisplayString()}."));
        }

        if (expectations.ExpectedSharedOpeningBalance is { } opening)
        {
            var account = document.Accounts.FirstOrDefault(item =>
                item.IsPrimary
                || (expectations.SharedAccountNameContains is { } token
                    && item.Name.Contains(token, StringComparison.OrdinalIgnoreCase)));

            var asOfOk = expectations.OpeningBalanceAsOf is not { } asOf
                         || account is { UpdatedOn: var updated } && updated == asOf;

            checks.Add(Check(
                "Shared opening balance is the recorded as-of figure",
                account is not null
                && account.CurrentBalance.Round().Amount == opening
                && asOfOk,
                account is null
                    ? "No shared account was found."
                    : $"{account.Name} {account.CurrentBalance.ToDisplayString()} on {account.UpdatedOn:yyyy-MM-dd}."));
        }

        if (expectations.ExpectedWeeklyBenefitTotal is { } weeklyBenefits)
        {
            var total = Money.Sum(document.Benefits
                .Where(benefit => benefit.PremiumFrequency == Frequency.Weekly)
                .Select(benefit => benefit.EmployeePremiumPerPeriod))
                .Round();

            checks.Add(Check(
                "Weekly employee benefits total the confirmed figure",
                total.Amount == weeklyBenefits,
                $"Stored weekly employee deductions {total.ToDisplayString()}."));

            checks.Add(Check(
                "Unconfirmed benefits are not applied to payroll",
                document.Benefits.Where(benefit => !benefit.IsConfirmed)
                    .All(benefit => !benefit.AppliesOn(today)),
                "Planned benefits remain inactive until confirmed."));
        }

        if (expectations.UnconfirmedPayAnchor is { } anchor
            && expectations.DoNotGeneratePayAfter is { } lastAllowed)
        {
            var generated = document.IncomeSources
                .Where(source => !source.PayScheduleConfirmed)
                .SelectMany(source => source.PayDates(lastAllowed.AddDays(1), lastAllowed.AddYears(1)))
                .ToList();

            checks.Add(Check(
                "Unconfirmed pay schedules do not invent later paydays",
                generated.Count == 0
                && document.IncomeSources.Any(source =>
                    !source.PayScheduleConfirmed && source.AnchorPayDate == anchor),
                generated.Count == 0
                    ? "No payday was generated after the confirmed anchor."
                    : $"{generated.Count} extra payday(s) were generated."));
        }

        var projection = CashFlowProjector.Project(new CashFlowInputs
        {
            From = today,
            To = today.AddDays(14),
            StartingBalance = document.PrimaryAccount?.CurrentBalance ?? Money.Zero,
            IncomeSources = document.IncomeSources,
            NetPayPerPeriod = TakeHomeCalculator.From(document, IncomeEstimate.Conservative, today)
                .NetPayPerPeriod,
            Expenses = document.Expenses,
            Assumption = IncomeEstimate.Conservative
        });

        if (expectations.HistoricalPayslipDate is { } historic
            && historic < today
            && expectations.ExpectedPayslipDeposit is { } historicDeposit)
        {
            var replayed = projection.Days
                .SelectMany(day => day.Events)
                .Any(item => item.Date == historic && item.Direction == CashFlowDirection.Deposit
                    && item.Amount.Amount == historicDeposit);

            checks.Add(Check(
                "Historical payslip is not replayed onto forward cash flow",
                !replayed,
                "Forward cash flow starts from the recorded opening balance."));
        }

        return new ImportVerificationReport(checks, preview);
    }

    private static bool IsTobaccoOrAlcohol(ExpenseItem expense) =>
        expense.Category.Name.Contains("tobacco", StringComparison.OrdinalIgnoreCase)
        || expense.Category.Name.Contains("cigarette", StringComparison.OrdinalIgnoreCase)
        || expense.Category.Name.Contains("alcohol", StringComparison.OrdinalIgnoreCase)
        || expense.Category.Name.Contains("beer", StringComparison.OrdinalIgnoreCase)
        || expense.Name.Contains("cigarette", StringComparison.OrdinalIgnoreCase)
        || expense.Name.Contains("beer", StringComparison.OrdinalIgnoreCase);

    private static ImportVerificationCheck Check(string name, bool passed, string detail) =>
        new(name, passed, detail);

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToLowerInvariant();
}
