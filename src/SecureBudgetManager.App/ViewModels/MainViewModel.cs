using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Services;

namespace SecureBudgetManager.App.ViewModels;

/// <summary>
/// The unlocked workspace: left navigation plus the current page.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IBudgetSession _session;

    public MainViewModel(
        IBudgetSession session,
        DashboardViewModel dashboard,
        HouseholdViewModel household,
        IncomeViewModel income,
        ExpensesViewModel expenses,
        PayrollViewModel payroll,
        CashFlowViewModel cashFlow,
        PlanningViewModel planning,
        DebtViewModel debt,
        SavingsViewModel savings,
        ActualsViewModel actuals,
        ThisWeekViewModel thisWeek,
        AllocationsViewModel allocations,
        GroceryPlanViewModel grocery,
        LocalGuidanceViewModel localGuidance,
        AllocationRulesViewModel allocationRules,
        ProductsViewModel products,
        InternationalTransfersViewModel internationalTransfers,
        ForeignAccountsViewModel foreignAccounts,
        SettingsViewModel settings,
        HelpViewModel help)
    {
        _session = session;
        Dashboard = dashboard;
        Household = household;
        Income = income;
        Expenses = expenses;
        Payroll = payroll;
        CashFlow = cashFlow;
        Planning = planning;
        Debt = debt;
        Savings = savings;
        Actuals = actuals;
        ThisWeek = thisWeek;
        Allocations = allocations;
        Grocery = grocery;
        LocalGuidance = localGuidance;
        AllocationRules = allocationRules;
        Products = products;
        InternationalTransfers = internationalTransfers;
        ForeignAccounts = foreignAccounts;
        Settings = settings;
        Help = help;
        currentViewModel = thisWeek;
        _session.Changed += OnSessionChanged;
    }

    /// <summary>Raised when the user presses Lock. The shell owns the actual locking.</summary>
    public event EventHandler? LockRequested;

    public DashboardViewModel Dashboard { get; }

    public HouseholdViewModel Household { get; }

    public IncomeViewModel Income { get; }

    public ExpensesViewModel Expenses { get; }

    public PayrollViewModel Payroll { get; }

    public CashFlowViewModel CashFlow { get; }

    public PlanningViewModel Planning { get; }

    public DebtViewModel Debt { get; }

    public SavingsViewModel Savings { get; }

    public ActualsViewModel Actuals { get; }

    public ThisWeekViewModel ThisWeek { get; }

    public AllocationsViewModel Allocations { get; }

    public GroceryPlanViewModel Grocery { get; }

    public LocalGuidanceViewModel LocalGuidance { get; }

    public AllocationRulesViewModel AllocationRules { get; }

    public ProductsViewModel Products { get; }

    public InternationalTransfersViewModel InternationalTransfers { get; }

    public ForeignAccountsViewModel ForeignAccounts { get; }

    public SettingsViewModel Settings { get; }

    public HelpViewModel Help { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardSelected))]
    [NotifyPropertyChangedFor(nameof(IsHouseholdSelected))]
    [NotifyPropertyChangedFor(nameof(IsIncomeSelected))]
    [NotifyPropertyChangedFor(nameof(IsExpensesSelected))]
    [NotifyPropertyChangedFor(nameof(IsPayrollSelected))]
    [NotifyPropertyChangedFor(nameof(IsCashFlowSelected))]
    [NotifyPropertyChangedFor(nameof(IsPlanningSelected))]
    [NotifyPropertyChangedFor(nameof(IsDebtSelected))]
    [NotifyPropertyChangedFor(nameof(IsSavingsSelected))]
    [NotifyPropertyChangedFor(nameof(IsActualsSelected))]
    [NotifyPropertyChangedFor(nameof(IsThisWeekSelected))]
    [NotifyPropertyChangedFor(nameof(IsAllocationsSelected))]
    [NotifyPropertyChangedFor(nameof(IsGrocerySelected))]
    [NotifyPropertyChangedFor(nameof(IsLocalGuidanceSelected))]
    [NotifyPropertyChangedFor(nameof(IsAllocationRulesSelected))]
    [NotifyPropertyChangedFor(nameof(IsProductsSelected))]
    [NotifyPropertyChangedFor(nameof(IsInternationalTransfersSelected))]
    [NotifyPropertyChangedFor(nameof(IsForeignAccountsSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSelected))]
    [NotifyPropertyChangedFor(nameof(IsHelpSelected))]
    [NotifyPropertyChangedFor(nameof(IsBenefitsSelected))]
    private PageViewModel currentViewModel;

    public bool IsDashboardSelected => CurrentViewModel is DashboardViewModel;

    public bool IsHouseholdSelected => CurrentViewModel is HouseholdViewModel;

    public bool IsIncomeSelected => CurrentViewModel is IncomeViewModel;

    public bool IsExpensesSelected => CurrentViewModel is ExpensesViewModel;

    public bool IsPayrollSelected => CurrentViewModel is PayrollViewModel && !_showBenefits;

    public bool IsCashFlowSelected => CurrentViewModel is CashFlowViewModel;

    public bool IsPlanningSelected => CurrentViewModel is PlanningViewModel;

    public bool IsDebtSelected => CurrentViewModel is DebtViewModel;

    public bool IsSavingsSelected => CurrentViewModel is SavingsViewModel;

    public bool IsActualsSelected => CurrentViewModel is ActualsViewModel;

    public bool IsThisWeekSelected => CurrentViewModel is ThisWeekViewModel;

    public bool IsAllocationsSelected => CurrentViewModel is AllocationsViewModel;

    public bool IsGrocerySelected => CurrentViewModel is GroceryPlanViewModel;

    public bool IsLocalGuidanceSelected => CurrentViewModel is LocalGuidanceViewModel;

    public bool IsAllocationRulesSelected => CurrentViewModel is AllocationRulesViewModel;

    public bool IsProductsSelected => CurrentViewModel is ProductsViewModel;

    public bool IsInternationalTransfersSelected => CurrentViewModel is InternationalTransfersViewModel;

    public bool IsForeignAccountsSelected => CurrentViewModel is ForeignAccountsViewModel;

    public bool IsSettingsSelected => CurrentViewModel is SettingsViewModel;

    public bool IsHelpSelected => CurrentViewModel is HelpViewModel;

    public bool IsBenefitsSelected => CurrentViewModel is PayrollViewModel && _showBenefits;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? statusMessage;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool HasUnsavedChanges => _session.HasUnsavedChanges;

    public bool IsSaving => _session.IsSaving;

    public bool CanSave => _session.IsOpen && _session.HasUnsavedChanges && !_session.IsSaving;

    [RelayCommand]
    private void NavigateToDashboard() => CurrentViewModel = Dashboard;

    [RelayCommand]
    private void NavigateToThisWeek() => CurrentViewModel = ThisWeek;

    [RelayCommand]
    private void NavigateToHousehold() => CurrentViewModel = Household;

    [RelayCommand]
    private void NavigateToIncome() => CurrentViewModel = Income;

    [RelayCommand]
    private void NavigateToExpenses() => CurrentViewModel = Expenses;

    [RelayCommand]
    private void NavigateToPayroll()
    {
        _showBenefits = false;
        CurrentViewModel = Payroll;
    }

    [RelayCommand]
    private void NavigateToBenefits()
    {
        _showBenefits = true;
        CurrentViewModel = Payroll;
        StatusMessage = "Benefits are in the dedicated Benefits section on the Payroll page.";
    }

    [RelayCommand]
    private void NavigateToCashFlow() => CurrentViewModel = CashFlow;

    [RelayCommand]
    private void NavigateToPlanning() => CurrentViewModel = Planning;

    [RelayCommand]
    private void NavigateToDebt() => CurrentViewModel = Debt;

    [RelayCommand]
    private void NavigateToSavings() => CurrentViewModel = Savings;

    [RelayCommand]
    private void NavigateToActuals() => CurrentViewModel = Actuals;

    [RelayCommand]
    private void NavigateToAllocations() => CurrentViewModel = Allocations;

    [RelayCommand]
    private void NavigateToGrocery() => CurrentViewModel = Grocery;

    [RelayCommand]
    private void NavigateToLocalGuidance() => CurrentViewModel = LocalGuidance;

    [RelayCommand]
    private void NavigateToAllocationRules() => CurrentViewModel = AllocationRules;

    [RelayCommand]
    private void NavigateToProducts() => CurrentViewModel = Products;

    [RelayCommand]
    private void NavigateToInternationalTransfers() => CurrentViewModel = InternationalTransfers;

    [RelayCommand]
    private void NavigateToForeignAccounts() => CurrentViewModel = ForeignAccounts;

    [RelayCommand]
    private void NavigateToSettings() => CurrentViewModel = Settings;

    [RelayCommand]
    private void NavigateToHelp() => CurrentViewModel = Help;

    [RelayCommand]
    private void Undo()
    {
        if (_session.Undo())
        {
            StatusMessage = "Last change undone. Save to write the restored document.";
            return;
        }

        StatusMessage = _session.LastError ?? "There is nothing to undo.";
    }

    [RelayCommand]
    private void LockWorkspace() => LockRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void DismissStatus() => StatusMessage = null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var saved = await _session.SaveAsync(cancellationToken);
        StatusMessage = saved
            ? "Saved to the local database."
            : _session.LastError ?? "The household data could not be saved.";
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(IsSaving));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    public void ResetNavigation()
    {
        _showBenefits = false;
        CurrentViewModel = ThisWeek;
        StatusMessage = null;
    }

    private bool _showBenefits;
}
