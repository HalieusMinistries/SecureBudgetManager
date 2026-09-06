using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Goals;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Savings;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.ViewModels;

public sealed record FundListItem(Guid Id, string Name, string Purpose, string Progress, string WeeklyNeed);

public sealed record GoalListItem(Guid Id, string Name, string Progress, string Deadline, string Note);

public sealed partial class SavingsViewModel : PageViewModel, IEditablePage
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;

    public SavingsViewModel(IBudgetSession session, IUserDialog dialog, TimeProvider clock)
        : base(
            "Savings and sinking funds",
            "Funds",
            "Required sinking funds and flexible savings are planned allocations, not leftover money. Emergency, repairs, deductibles and planned purchases stay on this device.")
    {
        _session = session;
        _dialog = dialog;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<FundPurpose>> PurposeOptions { get; } =
    [
        new(FundPurpose.EmergencyFund, "Emergency fund"),
        new(FundPurpose.CarRepairs, "Car-repair reserve"),
        new(FundPurpose.MedicalDeductible, "Medical deductible"),
        new(FundPurpose.CarRegistration, "Registration / insurance renewal"),
        new(FundPurpose.Holiday, "Christmas and gifts"),
        new(FundPurpose.AnnualSubscriptions, "Annual subscriptions"),
        new(FundPurpose.PlannedPurchase, "Planned purchase"),
        new(FundPurpose.VehicleReplacement, "Vehicle replacement"),
        new(FundPurpose.Custom, "Other")
    ];

    public IReadOnlyList<FundListItem> Funds { get; private set; } = [];

    public IReadOnlyList<GoalListItem> Goals { get; private set; } = [];

    public bool HasFunds => Funds.Count > 0;

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private FundPurpose purpose = FundPurpose.EmergencyFund;
    [ObservableProperty] private string currentBalance = "0";
    [ObservableProperty] private string targetAmount = string.Empty;
    [ObservableProperty] private DateTime? targetDate;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private string goalName = string.Empty;
    [ObservableProperty] private string goalTarget = string.Empty;
    [ObservableProperty] private string goalCurrent = "0";
    [ObservableProperty] private DateTime? goalDate;
    [ObservableProperty] private string goalContribution = "0";
    [ObservableProperty] private string conflictText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    [NotifyPropertyChangedFor(nameof(EditorSaveLabel))]
    [NotifyPropertyChangedFor(nameof(IsFundEditor))]
    private bool isEditorOpen;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    [NotifyPropertyChangedFor(nameof(EditorSaveLabel))]
    [NotifyPropertyChangedFor(nameof(IsFundEditor))]
    private bool isGoalEditor;
    private Guid? _editingFundId;
    private Guid? _editingGoalId;
    private string _originalFingerprint = string.Empty;

    public bool IsFundEditor => IsEditorOpen && !IsGoalEditor;

    public string EditorTitle => IsGoalEditor
        ? _editingGoalId is null ? "Add savings goal" : "Edit savings goal"
        : _editingFundId is null ? "Add sinking fund" : "Edit sinking fund";

    public string EditorSaveLabel => IsGoalEditor ? "Save goal" : "Save fund";

    public string? EditorEffectPreview =>
        string.IsNullOrWhiteSpace(ConflictText) ? null : ConflictText;

    public bool HasEditorChanges => IsEditorOpen && Fingerprint() != _originalFingerprint;

    public bool HasEditorError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? EditorError => ErrorMessage;

    public ICommand SaveEditorCommand => IsGoalEditor ? AddGoalCommand : AddFundCommand;

    public ICommand CancelEditorCommand => CancelEditorAliasCommand;

    public bool TryLeaveEditor()
    {
        if (!IsEditorOpen)
        {
            return true;
        }

        if (!HasEditorChanges || _dialog.Confirm("Unsaved changes", "Close without saving this fund or goal?"))
        {
            DismissEditor();
            return true;
        }

        return false;
    }

    public void DismissEditor()
    {
        IsEditorOpen = false;
        IsGoalEditor = false;
        _editingFundId = null;
        _editingGoalId = null;
        ErrorMessage = null;
    }

    private string Fingerprint() =>
        IsGoalEditor
            ? $"{GoalName}|{GoalTarget}|{GoalCurrent}|{GoalContribution}|{GoalDate}"
            : $"{Name}|{Purpose}|{CurrentBalance}|{TargetAmount}|{TargetDate}";

    [RelayCommand]
    private void CancelEditorAlias() => DismissEditor();

    [RelayCommand]
    private void BeginAddFund()
    {
        if (!TryLeaveEditor())
        {
            return;
        }

        _editingFundId = null;
        IsGoalEditor = false;
        Name = string.Empty;
        CurrentBalance = "0";
        TargetAmount = string.Empty;
        TargetDate = null;
        Purpose = FundPurpose.EmergencyFund;
        _originalFingerprint = Fingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void BeginAddGoal()
    {
        if (!TryLeaveEditor())
        {
            return;
        }

        _editingGoalId = null;
        IsGoalEditor = true;
        GoalName = string.Empty;
        GoalTarget = string.Empty;
        GoalCurrent = "0";
        GoalContribution = "0";
        GoalDate = null;
        _originalFingerprint = Fingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void BeginEditFund(FundListItem? item)
    {
        if (item is null || !_session.IsOpen || !TryLeaveEditor())
        {
            return;
        }

        var fund = _session.Document.Funds.FirstOrDefault(entry => entry.Id == item.Id);
        if (fund is null)
        {
            return;
        }

        _editingFundId = fund.Id;
        IsGoalEditor = false;
        Name = fund.Name;
        Purpose = fund.Purpose;
        CurrentBalance = AmountParsing.Format(fund.CurrentBalance);
        TargetAmount = fund.TargetAmount is { } target ? AmountParsing.Format(target) : string.Empty;
        TargetDate = fund.TargetDate?.ToDateTime(TimeOnly.MinValue);
        _originalFingerprint = Fingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void BeginEditGoal(GoalListItem? item)
    {
        if (item is null || !_session.IsOpen || !TryLeaveEditor())
        {
            return;
        }

        var goal = _session.Document.Goals.FirstOrDefault(entry => entry.Id == item.Id);
        if (goal is null)
        {
            return;
        }

        _editingGoalId = goal.Id;
        IsGoalEditor = true;
        GoalName = goal.Name;
        GoalTarget = AmountParsing.Format(goal.TargetAmount);
        GoalCurrent = AmountParsing.Format(goal.CurrentAmount);
        GoalContribution = AmountParsing.Format(goal.PlannedContribution);
        GoalDate = goal.TargetDate.ToDateTime(TimeOnly.MinValue);
        _originalFingerprint = Fingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private async Task AddFundAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name) || !AmountParsing.TryParseMoney(CurrentBalance, out var balance))
        {
            ErrorMessage = "Enter a fund name and current balance.";
            return;
        }

        Money? target = null;
        if (!string.IsNullOrWhiteSpace(TargetAmount))
        {
            if (!AmountParsing.TryParseMoney(TargetAmount, out var parsed))
            {
                ErrorMessage = "Enter a valid target amount.";
                return;
            }

            target = parsed;
        }

        var document = _session.Document;
        var funds = document.Funds.ToList();
        var fund = new SavingsFund
        {
            Id = _editingFundId ?? Guid.NewGuid(),
            Name = Name.Trim(),
            Purpose = Purpose,
            CurrentBalance = balance,
            TargetAmount = target,
            TargetDate = TargetDate is { } date ? DateOnly.FromDateTime(date) : null
        };
        var index = funds.FindIndex(entry => entry.Id == fund.Id);
        if (index >= 0)
        {
            funds[index] = fund;
        }
        else
        {
            funds.Add(fund);
        }

        if (!_session.TryReplace(document with { Funds = funds }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        var result = await EditorSaveCoordinator.PersistCurrentAsync(
            _session,
            cancellationToken,
            "Fund saved",
            saved => saved.Funds.Any(item => item.Id == fund.Id && item.Name == fund.Name));
        ErrorMessage = result.IsSuccess ? null : result.Message;
        if (!result.IsSuccess)
        {
            return;
        }

        Name = string.Empty;
        CurrentBalance = "0";
        TargetAmount = string.Empty;
        StatusMessage = result.Message;
        DismissEditor();
    }

    [RelayCommand]
    private async Task RemoveFundAsync(FundListItem? item, CancellationToken cancellationToken)
    {
        if (item is null || !_dialog.Confirm("Remove fund", $"Remove the fund named {item.Name}?"))
        {
            return;
        }

        var remaining = _session.Document.Funds.Where(fund => fund.Id != item.Id).ToList();
        if (!_session.TryReplace(_session.Document with { Funds = remaining }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        if (!await _session.SaveAsync(cancellationToken))
        {
            ErrorMessage = _session.LastError;
            return;
        }

        StatusMessage = "Fund removed.";
    }

    [RelayCommand]
    private async Task AddGoalAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(GoalName)
            || GoalDate is null
            || !AmountParsing.TryParseMoney(GoalTarget, out var target)
            || !AmountParsing.TryParseMoney(GoalCurrent, out var current)
            || !AmountParsing.TryParseMoney(GoalContribution, out var contribution)
            || target.IsNegative || current.IsNegative || contribution.IsNegative)
        {
            ErrorMessage = "Enter a goal name, target, current amount, weekly contribution and deadline.";
            return;
        }

        var goals = _session.Document.Goals.ToList();
        var goal = new Goal
        {
            Id = _editingGoalId ?? Guid.NewGuid(),
            Name = GoalName.Trim(),
            TargetAmount = target,
            CurrentAmount = current,
            TargetDate = DateOnly.FromDateTime(GoalDate.Value),
            PlannedContribution = contribution,
            ContributionFrequency = Frequency.Weekly
        };
        var index = goals.FindIndex(entry => entry.Id == goal.Id);
        if (index >= 0)
        {
            goals[index] = goal;
        }
        else
        {
            goals.Add(goal);
        }

        if (!_session.TryReplace(_session.Document with { Goals = goals }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        var result = await EditorSaveCoordinator.PersistCurrentAsync(
            _session,
            cancellationToken,
            "Goal saved",
            saved => saved.Goals.Any(item => item.Id == goal.Id && item.Name == goal.Name));
        ErrorMessage = result.IsSuccess ? null : result.Message;
        if (!result.IsSuccess)
        {
            return;
        }

        GoalName = string.Empty;
        GoalTarget = string.Empty;
        GoalCurrent = "0";
        GoalContribution = "0";
        StatusMessage = result.Message;
        DismissEditor();
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Funds = [];
            Goals = [];
            Name = string.Empty;
            CurrentBalance = string.Empty;
            TargetAmount = string.Empty;
            GoalName = string.Empty;
            GoalTarget = string.Empty;
            GoalCurrent = string.Empty;
            GoalContribution = string.Empty;
            ConflictText = string.Empty;
            StatusMessage = null;
            ErrorMessage = null;
            OnPropertyChanged(nameof(Funds));
            OnPropertyChanged(nameof(HasFunds));
            OnPropertyChanged(nameof(Goals));
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        Funds = _session.Document.Funds.Select(fund => new FundListItem(
            fund.Id,
            fund.Name,
            fund.Purpose.ToString(),
            $"{fund.ProgressPercent:0.#}% · {fund.CurrentBalance.ToDisplayString()}",
            fund.RequiredContribution(today, Frequency.Weekly).ToDisplayString() + " / week")).ToList();
        var reviews = GoalPlanner.Review(_session.Document.Goals, today, Frequency.Weekly);
        Goals = reviews.Select(status => new GoalListItem(
            status.Goal.Id,
            status.Goal.Name,
            $"{status.Goal.ProgressPercent:0.#}%",
            status.Goal.TargetDate.ToString("yyyy-MM-dd"),
            status.Explanation)).ToList();
        var takeHome = TakeHomeCalculator.From(_session.Document, IncomeEstimate.Conservative);
        var weeklyNet = BudgetOverviewCalculator.ToPeriod(takeHome.AnnualTakeHome, DisplayPeriod.Weekly);
        var weeklyEssentials = BudgetOverviewCalculator.ToPeriod(
            _session.Document.MonthlyEssentialSpending * 12m,
            DisplayPeriod.Weekly);
        var leftover = Money.Max(Money.Zero, (weeklyNet - weeklyEssentials).Round());
        var conflict = GoalPlanner.FindConflict(_session.Document.Goals, leftover, today, Frequency.Weekly);
        ConflictText = conflict is null
            ? Goals.Count == 0
                ? string.Empty
                : $"About {leftover.ToDisplayString()} a week is left after essential spending under the conservative estimate."
            : conflict.Explanation + " " + string.Join(" ", conflict.SuggestedTradeOffs);

        OnPropertyChanged(nameof(Funds));
        OnPropertyChanged(nameof(HasFunds));
        OnPropertyChanged(nameof(Goals));
    }
}
