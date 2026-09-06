using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.CashFlow;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Planning;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.ViewModels;

public sealed record ScenarioListItem(
    Guid Id,
    string Name,
    string Status,
    string Advertised,
    string Date,
    bool IsSelected = false);

public sealed record FindingRow(string Rule, string Result, string Explanation);

public sealed partial class PlanningViewModel : PageViewModel, IEditablePage
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;
    private Guid? _editingId;
    private string _originalFingerprint = string.Empty;

    public PlanningViewModel(IBudgetSession session, IUserDialog dialog, TimeProvider clock)
        : base(
            "Planning",
            "Centre",
            "Proposed purchases stay as scenarios until you convert them. Advertised payments are never treated as the true cost.")
    {
        _session = session;
        _dialog = dialog;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        ClearCarForm();
        Refresh();
    }

    public IReadOnlyList<ScenarioListItem> Scenarios { get; private set; } = [];

    [ObservableProperty] private string description = "Used car";
    [ObservableProperty] private DateTime? purchaseDate = DateTime.Today;
    [ObservableProperty] private string advertisedPayment = "235";
    [ObservableProperty] private string vehiclePrice = string.Empty;
    [ObservableProperty] private string cashDownPayment = string.Empty;
    [ObservableProperty] private string tradeInValue = string.Empty;
    [ObservableProperty] private string outstandingLoan = string.Empty;
    [ObservableProperty] private string salesTaxPercent = string.Empty;
    [ObservableProperty] private string dealerFees = string.Empty;
    [ObservableProperty] private string titleFees = string.Empty;
    [ObservableProperty] private string loanApr = string.Empty;
    [ObservableProperty] private string loanTermMonths = string.Empty;
    [ObservableProperty] private string insuranceIncrease = string.Empty;
    [ObservableProperty] private string insuranceDeductible = string.Empty;
    [ObservableProperty] private string milesPerMonth = string.Empty;
    [ObservableProperty] private string milesPerGallon = string.Empty;
    [ObservableProperty] private string fuelPrice = string.Empty;
    [ObservableProperty] private string annualMaintenance = string.Empty;
    [ObservableProperty] private string monthlyRepairReserve = string.Empty;
    [ObservableProperty] private string annualTyres = string.Empty;
    [ObservableProperty] private string annualRegistration = string.Empty;
    [ObservableProperty] private string annualInspection = string.Empty;
    [ObservableProperty] private string monthlyParking = string.Empty;
    [ObservableProperty] private string monthlyTolls = string.Empty;
    [ObservableProperty] private string roadsideAndGap = string.Empty;
    [ObservableProperty] private string annualDepreciationPercent = string.Empty;
    [ObservableProperty] private string monthlyReplacementFund = string.Empty;
    [ObservableProperty] private string monthlyOpportunityCost = string.Empty;
    [ObservableProperty] private string savingsInterestPercent = string.Empty;
    [ObservableProperty] private string replacedVehicleSaving = string.Empty;
    [ObservableProperty] private bool isUsedVehicle = true;

    [ObservableProperty] private string advertisedVsTrue = string.Empty;
    [ObservableProperty] private string expectedRating = string.Empty;
    [ObservableProperty] private string bestRating = string.Empty;
    [ObservableProperty] private string worstRating = string.Empty;
    [ObservableProperty] private string emergencyImpact = string.Empty;
    [ObservableProperty] private string cashFlowImpact = string.Empty;
    [ObservableProperty] private string derivedNotes = string.Empty;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    private bool isEditorOpen;
    [ObservableProperty] private double listScrollOffset;
    [ObservableProperty] private Guid? selectedRecordId;

    public IReadOnlyList<FindingRow> Findings { get; private set; } = [];

    public IReadOnlyList<string> WhatMustChange { get; private set; } = [];

    public bool HasScenarios => Scenarios.Count > 0;

    public string EditorTitle => _editingId is null ? "Add scenario" : "Edit scenario";

    public string EditorSaveLabel => "Save scenario";

    public string? EditorEffectPreview =>
        "Advertised payments are never treated as the true cost. Calculate, postpone or convert stay with this planner.";

    public bool HasEditorChanges => IsEditorOpen && ScenarioFingerprint() != _originalFingerprint;

    public bool HasEditorError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? EditorError => ErrorMessage;

    public ICommand SaveEditorCommand => SaveScenarioCommand;

    public ICommand CancelEditorCommand => CancelScenarioEditorCommand;

    public bool TryLeaveEditor()
    {
        if (!IsEditorOpen)
        {
            return true;
        }

        if (!HasEditorChanges || _dialog.Confirm("Unsaved changes", "Close without saving this scenario?"))
        {
            DismissEditor();
            return true;
        }

        return false;
    }

    public void DismissEditor()
    {
        IsEditorOpen = false;
        ErrorMessage = null;
    }

    private string ScenarioFingerprint() =>
        $"{Description}|{PurchaseDate}|{AdvertisedPayment}|{VehiclePrice}|{CashDownPayment}|{TradeInValue}|{OutstandingLoan}|{SalesTaxPercent}|{DealerFees}|{TitleFees}|{LoanApr}|{LoanTermMonths}|{InsuranceIncrease}|{InsuranceDeductible}|{MilesPerMonth}|{MilesPerGallon}|{FuelPrice}|{AnnualMaintenance}|{MonthlyRepairReserve}|{AnnualTyres}|{AnnualRegistration}|{AnnualInspection}|{MonthlyParking}|{MonthlyTolls}|{RoadsideAndGap}|{AnnualDepreciationPercent}|{MonthlyReplacementFund}|{MonthlyOpportunityCost}|{SavingsInterestPercent}|{ReplacedVehicleSaving}|{IsUsedVehicle}";

    [RelayCommand]
    private void CancelScenarioEditor() => DismissEditor();

    [RelayCommand]
    private void SelectScenario(ScenarioListItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedRecordId = item.Id;
        RematchSelection();
    }

    [RelayCommand]
    private void BeginAdd()
    {
        if (!TryLeaveEditor())
        {
            return;
        }

        _editingId = null;
        ClearCarForm();
        ClearResults();
        _originalFingerprint = ScenarioFingerprint();
        ErrorMessage = null;
        StatusMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void BeginEdit(ScenarioListItem? item)
    {
        if (item is null || !_session.IsOpen || !TryLeaveEditor())
        {
            return;
        }

        ApplyScenario(item);
        _originalFingerprint = ScenarioFingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void Recalculate()
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (!TryBuildInputs(out var inputs, out var error))
        {
            ErrorMessage = error;
            return;
        }

        var plan = CarPurchasePlanner.Build(inputs);
        var expected = Assess(plan.Scenario, ScenarioCase.Expected);
        var best = Assess(plan.Scenario, ScenarioCase.Best);
        var worst = Assess(plan.Scenario, ScenarioCase.Worst);
        var trueCost = expected.TrueCost;

        AdvertisedVsTrue =
            $"Advertised {trueCost.AdvertisedMonthly.ToDisplayString()} / month versus true {trueCost.MonthlyTrueCost.ToDisplayString()} / month " +
            $"({trueCost.AnnualTrueCost.ToDisplayString()} / year). Hidden monthly cost {trueCost.HiddenMonthlyCost.ToDisplayString()}.";
        ExpectedRating = $"{expected.RatingLabel}: {expected.Headline}";
        BestRating = $"Best case — {best.RatingLabel}: {best.Headline}";
        WorstRating = $"Worst case — {worst.RatingLabel}: {worst.Headline}";
        EmergencyImpact =
            $"Emergency fund after purchase: {expected.EmergencyFundAfterPurchase.ToDisplayString()} " +
            $"({expected.EmergencyFundMonthsAfterPurchase:0.#} months).";
        CashFlowImpact =
            $"Lowest projected balance with this purchase: {expected.LowestProjectedBalance.ToDisplayString()}.";
        Findings = expected.Findings.Select(finding => new FindingRow(
            finding.Rule,
            finding.Passed ? "Passed" : "Failed",
            finding.Explanation)).ToList();
        WhatMustChange = expected.WhatMustChange;
        DerivedNotes = string.Join(Environment.NewLine, plan.DerivedNotes);
        ErrorMessage = null;
        OnPropertyChanged(nameof(Findings));
        OnPropertyChanged(nameof(WhatMustChange));
    }

    [RelayCommand]
    private async Task SaveScenarioAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildInputs(out var inputs, out var error))
        {
            ErrorMessage = error;
            return;
        }

        var plan = CarPurchasePlanner.Build(inputs);
        var scenario = plan.Scenario with { Id = _editingId ?? Guid.NewGuid(), Status = ScenarioStatus.UnderConsideration };
        SelectedRecordId = scenario.Id;
        var document = _session.Document;
        var list = document.Scenarios.ToList();
        var index = list.FindIndex(item => item.Id == scenario.Id);
        if (index >= 0)
        {
            list[index] = scenario;
        }
        else
        {
            list.Add(scenario);
        }

        if (!await CommitAsync(document with { Scenarios = list }, cancellationToken))
        {
            return;
        }

        _editingId = scenario.Id;
        StatusMessage = "Scenario saved. It is not part of the live budget until you convert it.";
        _originalFingerprint = ScenarioFingerprint();
        Recalculate();
        RematchSelection();
    }

    [RelayCommand]
    private void LoadScenario(ScenarioListItem? item) => BeginEdit(item);

    private void ApplyScenario(ScenarioListItem item)
    {
        var scenario = _session.Document.Scenarios.FirstOrDefault(entry => entry.Id == item.Id);
        if (scenario is null)
        {
            return;
        }

        _editingId = scenario.Id;
        SelectedRecordId = scenario.Id;
        Description = scenario.Name;
        PurchaseDate = scenario.ProposedStartDate.ToDateTime(TimeOnly.MinValue);
        AdvertisedPayment = AmountParsing.Format(scenario.AdvertisedAmount);
        Recalculate();
        StatusMessage = "Loaded the saved scenario. Re-enter missing car fields if you want to recalculate the loan.";
        RematchSelection();
    }

    [RelayCommand]
    private async Task PostponeAsync(CancellationToken cancellationToken)
    {
        if (!await SetStatusAsync(ScenarioStatus.Postponed, cancellationToken))
        {
            return;
        }

        StatusMessage = "Scenario postponed. It remains saved and is not in the live budget.";
    }

    [RelayCommand]
    private async Task ConvertToBudgetAsync(CancellationToken cancellationToken)
    {
        if (_editingId is null)
        {
            ErrorMessage = "Save the scenario before converting it.";
            return;
        }

        if (!_dialog.Confirm("Convert to budget", "Add the recurring cash costs of this scenario to Expenses? One-off costs stay as a reminder in the scenario notes."))
        {
            return;
        }

        if (!TryBuildInputs(out var inputs, out var error))
        {
            ErrorMessage = error;
            return;
        }

        var plan = CarPurchasePlanner.Build(inputs);
        var scenario = plan.Scenario with { Id = _editingId.Value, Status = ScenarioStatus.ConvertedToBudget };
        var document = _session.Document;
        var expenses = document.Expenses.ToList();
        foreach (var line in scenario.Lines.Where(item => item.CountsTowardsCost && !item.IsNonCash && item.Frequency.IsRecurring()))
        {
            expenses.Add(new ExpenseItem
            {
                Id = Guid.NewGuid(),
                Name = $"{scenario.Name} — {line.Name}",
                Category = ExpenseCategory.Transport,
                ExpectedAmount = line.Amount,
                Frequency = line.Frequency,
                AnchorDueDate = scenario.ProposedStartDate,
                Necessity = ExpenseNecessity.Essential,
                Notes = "Converted from a planning scenario."
            });
        }

        var scenarios = document.Scenarios.Where(item => item.Id != scenario.Id).ToList();
        scenarios.Add(scenario);

        if (!await CommitAsync(document with { Expenses = expenses, Scenarios = scenarios }, cancellationToken))
        {
            return;
        }

        StatusMessage = "Recurring cash costs were added to Expenses. Review them there before the next payday.";
    }

    private async Task<bool> SetStatusAsync(ScenarioStatus status, CancellationToken cancellationToken)
    {
        if (_editingId is null)
        {
            ErrorMessage = "Save the scenario first.";
            return false;
        }

        var document = _session.Document;
        var scenarios = document.Scenarios.ToList();
        var index = scenarios.FindIndex(item => item.Id == _editingId.Value);
        if (index < 0)
        {
            ErrorMessage = "Save the scenario first.";
            return false;
        }

        scenarios[index] = scenarios[index] with { Status = status };
        return await CommitAsync(document with { Scenarios = scenarios }, cancellationToken);
    }

    private AffordabilityAssessment Assess(Scenario scenario, ScenarioCase scenarioCase)
    {
        var document = _session.Document;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var takeHome = TakeHomeCalculator.From(document, IncomeEstimate.Conservative);
        var extras = scenario.Lines
            .Where(line => line.CountsTowardsCost && !line.IsNonCash && line.Frequency.IsRecurring())
            .Select(line => new ExpenseItem
            {
                Id = Guid.NewGuid(),
                Name = line.Name,
                Category = ExpenseCategory.Transport,
                ExpectedAmount = line.Amount,
                Frequency = line.Frequency,
                AnchorDueDate = scenario.ProposedStartDate,
                Necessity = ExpenseNecessity.Essential
            })
            .ToList();

        var baseline = CashFlowProjector.Project(new CashFlowInputs
        {
            From = today,
            To = today.AddDays(90),
            StartingBalance = document.PrimaryAccount?.CurrentBalance ?? Money.Zero,
            IncomeSources = document.IncomeSources,
            NetPayPerPeriod = takeHome.NetPayPerPeriod,
            Expenses = document.Expenses,
            Assumption = IncomeEstimate.Conservative
        });

        var withScenario = CashFlowProjector.Project(new CashFlowInputs
        {
            From = today,
            To = today.AddDays(90),
            StartingBalance = document.PrimaryAccount?.CurrentBalance ?? Money.Zero,
            IncomeSources = document.IncomeSources,
            NetPayPerPeriod = takeHome.NetPayPerPeriod,
            Expenses = document.Expenses.Concat(extras).ToList(),
            Assumption = IncomeEstimate.Conservative
        });

        return AffordabilityEngine.Assess(new AffordabilityInputs
        {
            Scenario = scenario,
            Case = scenarioCase,
            ProjectionWithScenario = withScenario,
            BaselineProjection = baseline,
            EmergencyFundBalance = document.EmergencyFund?.CurrentBalance ?? Money.Zero,
            MonthlyEssentialSpending = document.MonthlyEssentialSpending,
            MonthlyNetIncome = BudgetOverviewCalculator.ToPeriod(takeHome.AnnualTakeHome, DisplayPeriod.AverageMonthly),
            ExistingMonthlyDebtPayments = document.MonthlyDebtPayments,
            MinimumBalanceReserve = document.Preferences.MinimumBalanceReserve,
            MinimumBreathingRoom = document.Preferences.MinimumBreathingRoom,
            EmergencyFundTargetMonths = document.Preferences.EmergencyFundTargetMonths,
            IncomeIsVariable = document.IncomeSources.OfType<HourlyIncome>().Any()
        });
    }

    private bool TryBuildInputs(out CarPurchaseInputs inputs, out string? error)
    {
        inputs = null!;
        if (string.IsNullOrWhiteSpace(Description))
        {
            error = "Name the proposed vehicle.";
            return false;
        }

        if (PurchaseDate is null)
        {
            error = "Choose a proposed purchase date.";
            return false;
        }

        if (!AmountParsing.TryParseMoney(AdvertisedPayment, out var advertised))
        {
            error = "Enter the advertised monthly payment.";
            return false;
        }

        if (OptionalDecimal(LoanTermMonths, out var term) && term is { } months && months is < 1 or > 120)
        {
            error = "Loan term must be between 1 and 120 months.";
            return false;
        }

        inputs = new CarPurchaseInputs
        {
            Description = Description.Trim(),
            PurchaseDate = DateOnly.FromDateTime(PurchaseDate.Value),
            AdvertisedMonthlyPayment = advertised,
            VehiclePrice = OptionalMoney(VehiclePrice),
            CashDownPayment = OptionalMoney(CashDownPayment),
            TradeInValue = OptionalMoney(TradeInValue),
            OutstandingLoanOnTradeIn = OptionalMoney(OutstandingLoan),
            SalesTaxPercent = OptionalDecimal(SalesTaxPercent, out var tax) ? tax : null,
            DealerAndDocumentFees = OptionalMoney(DealerFees),
            TitleAndRegistrationFees = OptionalMoney(TitleFees),
            LoanAnnualPercentageRate = OptionalDecimal(LoanApr, out var apr) ? apr : null,
            LoanTermMonths = OptionalDecimal(LoanTermMonths, out var termMonths) ? (int?)termMonths : null,
            MonthlyInsuranceIncrease = OptionalMoney(InsuranceIncrease),
            InsuranceDeductible = OptionalMoney(InsuranceDeductible),
            MilesDrivenPerMonth = OptionalDecimal(MilesPerMonth, out var miles) ? miles : null,
            MilesPerGallon = OptionalDecimal(MilesPerGallon, out var mpg) ? mpg : null,
            FuelPricePerGallon = OptionalMoney(FuelPrice),
            AnnualMaintenanceCost = OptionalMoney(AnnualMaintenance),
            MonthlyRepairReserve = CombinedReserve(),
            AnnualRegistrationRenewal = OptionalMoney(AnnualRegistration),
            AnnualInspectionCost = OptionalMoney(AnnualInspection),
            MonthlyParkingCost = OptionalMoney(MonthlyParking),
            MonthlyTollsAndWashes = OptionalMoney(MonthlyTolls),
            MonthlyFinancingExtras = OptionalMoney(RoadsideAndGap),
            AnnualDepreciationPercent = OptionalDecimal(AnnualDepreciationPercent, out var dep) ? dep : null,
            MonthlyReplacementFund = OptionalMoney(MonthlyReplacementFund),
            MonthlyOpportunityCost = OptionalMoney(MonthlyOpportunityCost),
            SavingsInterestRatePercent = OptionalDecimal(SavingsInterestPercent, out var interest) ? interest : null,
            MonthlySavingFromReplacedVehicle = OptionalMoney(ReplacedVehicleSaving),
            IsUsedVehicle = IsUsedVehicle
        };
        error = null;
        return true;
    }

    private Money? CombinedReserve()
    {
        var reserve = OptionalMoney(MonthlyRepairReserve);
        var tyres = OptionalMoney(AnnualTyres);
        if (tyres is { } annualTyres)
        {
            var monthlyTyres = (annualTyres / 12m).Round();
            reserve = reserve is { } existing ? (existing + monthlyTyres).Round() : monthlyTyres;
        }

        return reserve;
    }

    private static Money? OptionalMoney(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : AmountParsing.TryParseMoney(text, out var money) ? money : null;

    private static bool OptionalDecimal(string text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!AmountParsing.TryParseDecimal(text, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private async Task<bool> CommitAsync(SecureBudgetManager.Core.Storage.BudgetDocument document, CancellationToken cancellationToken)
    {
        var result = await EditorSaveCoordinator.TryCommitAsync(
            _session,
            document,
            cancellationToken,
            "Scenario saved");
        ErrorMessage = result.IsSuccess ? null : result.Message;
        return result.IsSuccess;
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Scenarios = [];
            Findings = [];
            WhatMustChange = [];
            AdvertisedVsTrue = string.Empty;
            ExpectedRating = string.Empty;
            BestRating = string.Empty;
            WorstRating = string.Empty;
            EmergencyImpact = string.Empty;
            CashFlowImpact = string.Empty;
            DerivedNotes = string.Empty;
            StatusMessage = null;
            ErrorMessage = null;
            _editingId = null;
            SelectedRecordId = null;
            ClearCarForm();
            DismissEditor();
            OnPropertyChanged(nameof(Scenarios));
            OnPropertyChanged(nameof(Findings));
            OnPropertyChanged(nameof(WhatMustChange));
            OnPropertyChanged(nameof(HasScenarios));
            return;
        }

        Scenarios = _session.Document.Scenarios
            .Select(item => new ScenarioListItem(
                item.Id,
                item.Name,
                item.Status.ToString(),
                item.AdvertisedAmount.ToDisplayString(),
                item.ProposedStartDate.ToString("yyyy-MM-dd"),
                item.Id == SelectedRecordId))
            .ToList();
        OnPropertyChanged(nameof(Scenarios));
        OnPropertyChanged(nameof(HasScenarios));
    }

    private void RematchSelection()
    {
        Scenarios = Scenarios
            .Select(item => item with { IsSelected = item.Id == SelectedRecordId })
            .ToList();
        OnPropertyChanged(nameof(Scenarios));
        OnPropertyChanged(nameof(HasScenarios));
    }

    private void ClearResults()
    {
        Findings = [];
        WhatMustChange = [];
        AdvertisedVsTrue = string.Empty;
        ExpectedRating = string.Empty;
        BestRating = string.Empty;
        WorstRating = string.Empty;
        EmergencyImpact = string.Empty;
        CashFlowImpact = string.Empty;
        DerivedNotes = string.Empty;
        OnPropertyChanged(nameof(Findings));
        OnPropertyChanged(nameof(WhatMustChange));
    }

    private void ClearCarForm()
    {
        Description = string.Empty;
        PurchaseDate = null;
        AdvertisedPayment = string.Empty;
        VehiclePrice = string.Empty;
        CashDownPayment = string.Empty;
        TradeInValue = string.Empty;
        OutstandingLoan = string.Empty;
        SalesTaxPercent = string.Empty;
        DealerFees = string.Empty;
        TitleFees = string.Empty;
        LoanApr = string.Empty;
        LoanTermMonths = string.Empty;
        InsuranceIncrease = string.Empty;
        InsuranceDeductible = string.Empty;
        MilesPerMonth = string.Empty;
        MilesPerGallon = string.Empty;
        FuelPrice = string.Empty;
        AnnualMaintenance = string.Empty;
        MonthlyRepairReserve = string.Empty;
        AnnualTyres = string.Empty;
        AnnualRegistration = string.Empty;
        AnnualInspection = string.Empty;
        MonthlyParking = string.Empty;
        MonthlyTolls = string.Empty;
        RoadsideAndGap = string.Empty;
        AnnualDepreciationPercent = string.Empty;
        MonthlyReplacementFund = string.Empty;
        MonthlyOpportunityCost = string.Empty;
        SavingsInterestPercent = string.Empty;
        ReplacedVehicleSaving = string.Empty;
        IsUsedVehicle = true;
    }
}
