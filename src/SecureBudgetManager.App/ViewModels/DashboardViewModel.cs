using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Models;

namespace SecureBudgetManager.App.ViewModels;

public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly IBudgetSession _session;
    private readonly TimeProvider _clock;

    public DashboardViewModel(IBudgetSession session, TimeProvider clock)
        : base(
            "Dashboard",
            "Overview",
            "Money available now is cash already recorded. Average monthly surplus is a future forecast and is not spendable today.")
    {
        _session = session;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWeekly))]
    [NotifyPropertyChangedFor(nameof(IsAverageMonthly))]
    [NotifyPropertyChangedFor(nameof(IsAnnual))]
    private DisplayPeriod period = DisplayPeriod.AverageMonthly;

    public bool IsWeekly => Period == DisplayPeriod.Weekly;

    public bool IsAverageMonthly => Period == DisplayPeriod.AverageMonthly;

    public bool IsAnnual => Period == DisplayPeriod.Annual;

    [ObservableProperty]
    private BudgetOverview overview = BudgetOverviewCalculator.Locked();

    [ObservableProperty]
    private string availableNowText = string.Empty;

    [ObservableProperty]
    private string reservedText = string.Empty;

    [ObservableProperty]
    private string safeToSpendNowText = string.Empty;

    [ObservableProperty]
    private string nextIncomeText = string.Empty;

    [ObservableProperty]
    private string spendingSafetyNotice = string.Empty;

    public IReadOnlyList<AttentionItem> Attention => Overview.Attention;

    public bool HasAttention => Overview.HasAttention;

    public bool IsUnlocked => _session.IsOpen;

    [RelayCommand]
    private void ShowWeekly() => Period = DisplayPeriod.Weekly;

    [RelayCommand]
    private void ShowAverageMonthly() => Period = DisplayPeriod.AverageMonthly;

    [RelayCommand]
    private void ShowAnnual() => Period = DisplayPeriod.Annual;

    partial void OnPeriodChanged(DisplayPeriod value) => Refresh();

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Overview = BudgetOverviewCalculator.Locked();
            AvailableNowText = string.Empty;
            ReservedText = string.Empty;
            SafeToSpendNowText = string.Empty;
            NextIncomeText = string.Empty;
            SpendingSafetyNotice = string.Empty;
            OnPropertyChanged(nameof(Attention));
            OnPropertyChanged(nameof(HasAttention));
            OnPropertyChanged(nameof(IsUnlocked));
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        Overview = BudgetOverviewCalculator.Build(_session.Document, Period, today);
        var position = OperationalPositionCalculator.Build(_session.Document, today);
        AvailableNowText = position.AvailableNow.ToDisplayString();
        ReservedText = position.Reserved.ToDisplayString();
        SafeToSpendNowText = position.SafeToSpend.ToDisplayString();
        NextIncomeText = position.NextIncomeText;
        SpendingSafetyNotice = position.SafetyNotice;
        OnPropertyChanged(nameof(Attention));
        OnPropertyChanged(nameof(HasAttention));
        OnPropertyChanged(nameof(IsUnlocked));
    }
}
