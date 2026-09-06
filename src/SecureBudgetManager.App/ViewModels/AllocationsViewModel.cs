using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Expenses;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.ViewModels;

public sealed record ReservationRow(
    Guid ObligationId,
    ObligationKind Kind,
    string Name,
    string Owner,
    string Classification,
    string Category,
    string Priority,
    string Frequency,
    string DueDate,
    string AmountDue,
    string AmountPaid,
    string AlreadyReserved,
    string StillRequired,
    string RequiredNow,
    string PaydaysRemaining,
    string Status,
    string Tier,
    string Consequence,
    string Explanation,
    bool IsUnassigned,
    bool IsSelected = false);

public sealed record SplitRow(
    string Name,
    string Percent,
    string Amount,
    string DefaultAmount,
    string Difference,
    string RoundingNote);

public sealed record TransferRow(
    Guid Id,
    string From,
    string To,
    string Amount,
    string Date,
    string Purpose,
    string Recurrence,
    bool IsSelected = false);

public sealed record PayerChoice(
    string Key,
    string Name,
    BillAssignment Assignment,
    Guid? MemberId);

/// <summary>
/// Where the household sets aside money against named obligations, sees how shared costs are
/// divided, and records any deliberate transfer between two people's personal balances.
/// </summary>
public sealed partial class AllocationsViewModel : PageViewModel, IEditablePage
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;
    private Guid? _editingTransferId;
    private string _originalFingerprint = string.Empty;
    private int _saveDepth;

    public AllocationsViewModel(IBudgetSession session, IUserDialog dialog, TimeProvider clock)
        : base(
            "Bills & Reservations",
            "Obligations",
            "Paid, reserved and still owed stay separate. Reserved money remains in the bank " +
            "but is excluded from safe-to-spend.")
    {
        _session = session;
        _dialog = dialog;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<Frequency>> TransferFrequencies { get; } = FrequencyChoices.Recurring;

    public IReadOnlyList<ChoiceOption<BillAssignment>> SharedSplitOptions { get; } =
    [
        new(BillAssignment.PercentageSplit, "Percentage split"),
        new(BillAssignment.FixedDollarSplit, "Fixed dollar split"),
        new(BillAssignment.EnteredContributions, "Separate entered contributions")
    ];

    [ObservableProperty]
    private ReservationRow? selectedReservation;

    [ObservableProperty]
    private string reserveAmount = string.Empty;

    [ObservableProperty]
    private bool reserveIsProtected = true;

    [ObservableProperty]
    private string splitSummary = string.Empty;

    [ObservableProperty]
    private string overrideSummary = string.Empty;

    [ObservableProperty]
    private string roundingSummary = string.Empty;

    [ObservableProperty]
    private string totals = string.Empty;

    [ObservableProperty]
    private HouseholdMember? transferFrom;

    [ObservableProperty]
    private HouseholdMember? transferTo;

    [ObservableProperty]
    private string transferAmount = string.Empty;

    [ObservableProperty]
    private DateTime? transferDate;

    [ObservableProperty]
    private string transferPurpose = string.Empty;

    [ObservableProperty]
    private bool transferIsRecurring;

    [ObservableProperty]
    private Frequency transferFrequency = Frequency.Weekly;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    [NotifyPropertyChangedFor(nameof(EditorSaveLabel))]
    [NotifyPropertyChangedFor(nameof(IsBillEditor))]
    private bool isEditorOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    [NotifyPropertyChangedFor(nameof(EditorSaveLabel))]
    [NotifyPropertyChangedFor(nameof(IsBillEditor))]
    private bool isTransferEditor;

    [ObservableProperty]
    private string paymentAmount = string.Empty;

    [ObservableProperty]
    private BillAssignment editorAssignment = BillAssignment.Unassigned;

    [ObservableProperty]
    private HouseholdMember? editorPayer;

    [ObservableProperty]
    private string editorFirstShare = "50";

    [ObservableProperty]
    private string editorSecondShare = "50";

    [ObservableProperty]
    private string assignmentPreview = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSharedHousehold))]
    private string selectedPayerKey = "unassigned";

    [ObservableProperty]
    private BillAssignment sharedSplitMethod = BillAssignment.PercentageSplit;

    [ObservableProperty]
    private string transferPreview = string.Empty;

    [ObservableProperty]
    private double listScrollOffset;

    [ObservableProperty]
    private Guid? selectedRecordId;

    public IReadOnlyList<PayerChoice> PayerChoices { get; private set; } =
        [new("unassigned", "Unassigned", BillAssignment.Unassigned, null)];

    public bool HasReservations => Reservations.Count > 0;

    public bool HasTransfers => Transfers.Count > 0;

    public bool IsBillEditor => IsEditorOpen && !IsTransferEditor;

    public bool IsSharedHousehold => SelectedPayerKey == "shared";

    public string FirstAdultShareLabel =>
        Adults.Count > 0 ? $"{Adults[0].Name}'s share" : "First adult share";

    public string SecondAdultShareLabel =>
        Adults.Count > 1 ? $"{Adults[1].Name}'s share" : "Second adult share";

    public string EditorTitle => IsTransferEditor
        ? _editingTransferId is null ? "Add transfer" : "Edit transfer"
        : SelectedReservation?.Name ?? "Bill";

    public string EditorSaveLabel => IsTransferEditor ? "Save transfer" : "Save assignment";

    public string? EditorEffectPreview =>
        IsTransferEditor
            ? string.IsNullOrWhiteSpace(TransferPreview) ? null : TransferPreview
            : string.IsNullOrWhiteSpace(AssignmentPreview) ? null : AssignmentPreview;

    public bool HasEditorChanges => IsEditorOpen && Fingerprint() != _originalFingerprint;

    public bool HasEditorError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? EditorError => ErrorMessage;

    public ICommand SaveEditorCommand => IsTransferEditor ? AddTransferCommand : SaveAssignmentCommand;

    public ICommand CancelEditorCommand => CancelEditorAliasCommand;

    public bool TryLeaveEditor()
    {
        if (!IsEditorOpen)
        {
            return true;
        }

        if (!HasEditorChanges
            || _dialog.Confirm("Unsaved changes", "Close without saving this bill or transfer?"))
        {
            DismissEditor();
            return true;
        }

        return false;
    }

    public void DismissEditor()
    {
        IsEditorOpen = false;
        IsTransferEditor = false;
        _editingTransferId = null;
        PaymentAmount = string.Empty;
        AssignmentPreview = string.Empty;
        TransferPreview = string.Empty;
        ErrorMessage = null;
    }

    public IReadOnlyList<ReservationRow> Reservations { get; private set; } = [];

    public IReadOnlyList<SplitRow> Shares { get; private set; } = [];

    public IReadOnlyList<TransferRow> Transfers { get; private set; } = [];

    public IReadOnlyList<HouseholdMember> Members { get; private set; } = [];

    public IReadOnlyList<HouseholdMember> Adults { get; private set; } = [];

    public IReadOnlyList<string> SplitWarnings { get; private set; } = [];

    private string Fingerprint() =>
        IsTransferEditor
            ? $"{TransferFrom?.Id}|{TransferTo?.Id}|{TransferAmount}|{TransferDate}|{TransferPurpose}|{TransferIsRecurring}|{TransferFrequency}"
            : $"{SelectedReservation?.ObligationId}|{EditorAssignment}|{EditorPayer?.Id}|{EditorFirstShare}|{EditorSecondShare}|{SelectedPayerKey}|{SharedSplitMethod}";

    [RelayCommand]
    private void CancelEditorAlias() => DismissEditor();

    [RelayCommand]
    private void SelectBill(ReservationRow? row)
    {
        if (row is null)
        {
            return;
        }

        SelectedRecordId = row.ObligationId;
        SelectedReservation = Reservations.FirstOrDefault(item => item.ObligationId == row.ObligationId);
        RematchSelection();
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void SelectTransfer(TransferRow? row)
    {
        if (row is null)
        {
            return;
        }

        SelectedRecordId = row.Id;
        RematchSelection();
    }

    [RelayCommand]
    private void OpenBill(ReservationRow? row)
    {
        if (row is null || !TryLeaveEditor())
        {
            return;
        }

        SelectedRecordId = row.ObligationId;
        SelectedReservation = Reservations.FirstOrDefault(item => item.ObligationId == row.ObligationId) ?? row;
        IsTransferEditor = false;
        LoadAssignment(SelectedReservation);
        AssignmentPreview = string.Empty;
        ErrorMessage = null;
        _originalFingerprint = Fingerprint();
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorEffectPreview));
        RematchSelection();
    }

    [RelayCommand]
    private void BeginAddTransfer()
    {
        if (!TryLeaveEditor())
        {
            return;
        }

        _editingTransferId = null;
        IsTransferEditor = true;
        TransferFrom = null;
        TransferTo = null;
        TransferAmount = string.Empty;
        TransferDate = null;
        TransferPurpose = string.Empty;
        TransferIsRecurring = false;
        TransferFrequency = Frequency.Weekly;
        TransferPreview = string.Empty;
        ErrorMessage = null;
        _originalFingerprint = Fingerprint();
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorEffectPreview));
    }

    [RelayCommand]
    private void BeginEditTransfer(TransferRow? row)
    {
        if (row is null || !_session.IsOpen || !TryLeaveEditor())
        {
            return;
        }

        var transfer = _session.Document.Transfers.FirstOrDefault(item => item.Id == row.Id);
        if (transfer is null)
        {
            return;
        }

        SelectedRecordId = transfer.Id;
        _editingTransferId = transfer.Id;
        IsTransferEditor = true;
        TransferFrom = Members.FirstOrDefault(member => member.Id == transfer.FromMemberId);
        TransferTo = Members.FirstOrDefault(member => member.Id == transfer.ToMemberId);
        TransferAmount = AmountParsing.Format(transfer.Amount);
        TransferDate = transfer.Date.ToDateTime(TimeOnly.MinValue);
        TransferPurpose = transfer.Purpose ?? string.Empty;
        TransferIsRecurring = transfer.IsRecurring;
        TransferFrequency = transfer.IsRecurring ? transfer.RecurringFrequency : Frequency.Weekly;
        PreviewTransfer();
        ErrorMessage = null;
        _originalFingerprint = Fingerprint();
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
        RematchSelection();
    }

    private void LoadAssignment(ReservationRow row)
    {
        if (!_session.IsOpen)
        {
            EditorAssignment = BillAssignment.Unassigned;
            EditorPayer = null;
            SelectedPayerKey = "unassigned";
            return;
        }

        var expense = _session.Document.Expenses.FirstOrDefault(item => item.Id == row.ObligationId);
        if (expense is null)
        {
            EditorAssignment = row.IsUnassigned ? BillAssignment.Unassigned : EditorAssignment;
            SelectedPayerKey = "unassigned";
            return;
        }

        EditorAssignment = expense.Assignment;
        var split = expense.Split;
        EditorPayer = split is { Participants.Count: > 0 }
            ? Members.FirstOrDefault(member => member.Id == split.Participants[0])
            : null;

        if (split?.Method == SplitMethod.Percentage && split.Percentages.Count >= 2)
        {
            EditorFirstShare = split.Percentages[0].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            EditorSecondShare = split.Percentages[1].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (split?.Method == SplitMethod.FixedAmount && split.FixedAmounts.Count >= 2)
        {
            EditorFirstShare = AmountParsing.Format(split.FixedAmounts[0]);
            EditorSecondShare = AmountParsing.Format(split.FixedAmounts[1]);
        }
        else
        {
            EditorFirstShare = "50";
            EditorSecondShare = "50";
        }

        if (expense.Assignment is BillAssignment.PercentageSplit
            or BillAssignment.FixedDollarSplit
            or BillAssignment.EnteredContributions)
        {
            SharedSplitMethod = expense.Assignment;
        }

        SelectedPayerKey = expense.Assignment switch
        {
            BillAssignment.MemberPaysAll when EditorPayer is { } payer => $"member:{payer.Id}",
            BillAssignment.PercentageSplit or BillAssignment.FixedDollarSplit
                or BillAssignment.EnteredContributions => "shared",
            BillAssignment.SharedAccount => "account",
            _ => "unassigned"
        };
    }

    partial void OnSelectedPayerKeyChanged(string value)
    {
        var choice = PayerChoices.FirstOrDefault(item => item.Key == value);
        if (choice is null)
        {
            return;
        }

        if (choice.Assignment == BillAssignment.MemberPaysAll)
        {
            EditorAssignment = BillAssignment.MemberPaysAll;
            EditorPayer = Members.FirstOrDefault(member => member.Id == choice.MemberId);
            return;
        }

        if (choice.Key == "shared")
        {
            EditorAssignment = SharedSplitMethod;
            return;
        }

        EditorAssignment = choice.Assignment;
    }

    partial void OnSharedSplitMethodChanged(BillAssignment value)
    {
        if (SelectedPayerKey == "shared")
        {
            EditorAssignment = value;
        }
    }

    [RelayCommand]
    private async Task RecordPaymentAsync(CancellationToken cancellationToken)
    {
        if (!TryBeginSave())
        {
            return;
        }

        try
        {
            if (!_session.IsOpen || SelectedReservation is not { } selected)
            {
                ErrorMessage = "Open a bill first.";
                return;
            }

            if (!AmountParsing.TryParseMoney(PaymentAmount, out var amount) || amount.IsZero || amount.IsNegative)
            {
                ErrorMessage = "Enter the amount that was paid.";
                return;
            }

            var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
            var document = _session.Document;
            var expense = document.Expenses.FirstOrDefault(item => item.Id == selected.ObligationId);
            var transactions = document.Transactions.ToList();
            transactions.Add(new ExpenseTransaction
            {
                Id = Guid.NewGuid(),
                ExpenseItemId = selected.Kind == ObligationKind.Expense ? selected.ObligationId : null,
                Date = today,
                Description = $"{selected.Name} payment",
                Amount = amount,
                Category = expense?.Category ?? ExpenseCategory.Other,
                IsConfirmed = true,
                Notes = "Recorded from Bills & Reservations."
            });

            if (!_session.TryReplace(document with { Transactions = transactions }, out var error))
            {
                ErrorMessage = error;
                return;
            }

            var result = await EditorSaveCoordinator.PersistCurrentAsync(
                _session,
                cancellationToken,
                "Payment recorded",
                document => document.Transactions.Any(item =>
                    item.Amount == amount && item.Description == $"{selected.Name} payment"));
            ErrorMessage = result.IsSuccess ? null : result.Message;
            StatusMessage = result.IsSuccess ? result.Message : StatusMessage;
            if (result.IsSuccess)
            {
                PaymentAmount = string.Empty;
                DismissEditor();
            }
        }
        finally
        {
            EndSave();
        }
    }

    [RelayCommand]
    private async Task SaveReserveAsync(CancellationToken cancellationToken)
    {
        if (!TryBeginSave())
        {
            return;
        }

        try
        {
            if (!_session.IsOpen)
            {
                ErrorMessage = "Open the household database first.";
                return;
            }

            if (SelectedReservation is not { } selected)
            {
                ErrorMessage = "Choose the obligation the money is set aside for.";
                return;
            }

            if (!AmountParsing.TryParseMoney(ReserveAmount, out var amount) || amount.IsNegative)
            {
                ErrorMessage = "Enter the amount already set aside.";
                return;
            }

            var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
            var document = _session.Document;

            var reserves = document.Reserves
                .Where(reserve => reserve.ObligationId != selected.ObligationId)
                .ToList();

            reserves.Add(new ObligationReserve
            {
                Id = Guid.NewGuid(),
                ObligationId = selected.ObligationId,
                Kind = selected.Kind,
                Reserved = amount,
                UpdatedOn = today,
                IsProtected = ReserveIsProtected
            });

            if (!_session.TryReplace(document with { Reserves = reserves }, out var error))
            {
                ErrorMessage = error;
                return;
            }

            var result = await EditorSaveCoordinator.PersistCurrentAsync(
                _session,
                cancellationToken,
                "Reservation saved",
                document => document.Reserves.Any(item =>
                    item.ObligationId == selected.ObligationId && item.Reserved == amount));
            ErrorMessage = result.IsSuccess ? null : result.Message;
            StatusMessage = result.IsSuccess ? result.Message : StatusMessage;
            if (result.IsSuccess)
            {
                ReserveAmount = string.Empty;
                DismissEditor();
            }
        }
        finally
        {
            EndSave();
        }
    }

    [RelayCommand]
    private void PreviewAssignment()
    {
        if (!_session.IsOpen || SelectedReservation is null)
        {
            AssignmentPreview = "Select a bill first.";
            OnPropertyChanged(nameof(EditorEffectPreview));
            return;
        }

        if (!TryBuildAssignment(out var assignment, out var split, out var error))
        {
            AssignmentPreview = error;
            OnPropertyChanged(nameof(EditorEffectPreview));
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var preview = BillAssignmentPlanner.Preview(
            _session.Document,
            today,
            SelectedReservation.ObligationId,
            assignment,
            split);
        AssignmentPreview =
            preview.Summary + Environment.NewLine +
            string.Join(
                Environment.NewLine,
                preview.People.Select(person =>
                    $"{person.Name}: {person.Before.ToDisplayString()} now, {person.After.ToDisplayString()} after " +
                    $"({person.Change.ToDisplayString()}).")) +
            Environment.NewLine +
            $"Unassigned household total {preview.UnassignedBefore.ToDisplayString()} → {preview.UnassignedAfter.ToDisplayString()}." +
            Environment.NewLine +
            preview.Risk;
        OnPropertyChanged(nameof(EditorEffectPreview));
    }

    [RelayCommand]
    private void PreviewTransfer()
    {
        if (TransferFrom is not { } from || TransferTo is not { } to)
        {
            TransferPreview = "Choose two different people to see the effect on both personal balances.";
            OnPropertyChanged(nameof(EditorEffectPreview));
            return;
        }

        if (from.Id == to.Id)
        {
            TransferPreview = "A transfer needs two different people.";
            OnPropertyChanged(nameof(EditorEffectPreview));
            return;
        }

        if (!AmountParsing.TryParseMoney(TransferAmount, out var amount) || amount.IsZero || amount.IsNegative)
        {
            TransferPreview =
                $"A confirmed transfer moves money only between {from.Name} and {to.Name}. " +
                "Household available money is not changed automatically.";
            OnPropertyChanged(nameof(EditorEffectPreview));
            return;
        }

        var buckets = BillAssignmentPlanner.AccountBalances(_session.IsOpen ? _session.Document : new());
        var fromBefore = buckets.Personal.TryGetValue(from.Id, out var fromBalance) ? fromBalance : Money.Zero;
        var toBefore = buckets.Personal.TryGetValue(to.Id, out var toBalance) ? toBalance : Money.Zero;
        TransferPreview =
            $"Move {amount.ToDisplayString()} from {from.Name} to {to.Name}. " +
            $"{from.Name}: {fromBefore.ToDisplayString()} → {(fromBefore - amount).Round().ToDisplayString()}. " +
            $"{to.Name}: {toBefore.ToDisplayString()} → {(toBefore + amount).Round().ToDisplayString()}. " +
            "This changes only those two personal balances after you confirm.";
        OnPropertyChanged(nameof(EditorEffectPreview));
    }

    [RelayCommand]
    private async Task SaveAssignmentAsync(CancellationToken cancellationToken)
    {
        if (!TryBeginSave())
        {
            return;
        }

        try
        {
            if (!_session.IsOpen || SelectedReservation is null)
            {
                ErrorMessage = "Select a bill first.";
                return;
            }

            if (!TryBuildAssignment(out var assignment, out var split, out var error))
            {
                ErrorMessage = error;
                return;
            }

            PreviewAssignment();
            if (!_dialog.Confirm(
                    "Save bill assignment",
                    (string.IsNullOrWhiteSpace(AssignmentPreview)
                        ? "Save this assignment?"
                        : AssignmentPreview) +
                    Environment.NewLine +
                    "Unassigned bills are not deducted from either person."))
            {
                return;
            }

            var document = _session.Document;
            var expenses = document.Expenses.Select(expense =>
                expense.Id == SelectedReservation.ObligationId
                    ? BillAssignmentPlanner.Apply(expense, assignment, split)
                    : expense).ToList();

            if (!_session.TryReplace(document with { Expenses = expenses }, out var replaceError))
            {
                ErrorMessage = replaceError;
                return;
            }

            var obligationId = SelectedReservation.ObligationId;
            var result = await EditorSaveCoordinator.PersistCurrentAsync(
                _session,
                cancellationToken,
                "Bill assignment saved",
                document => document.Expenses.Any(item =>
                    item.Id == obligationId && item.Assignment == assignment));
            ErrorMessage = result.IsSuccess ? null : result.Message;
            StatusMessage = result.IsSuccess ? result.Message : StatusMessage;
            if (result.IsSuccess)
            {
                DismissEditor();
            }
        }
        finally
        {
            EndSave();
        }
    }

    public bool TryBuildAssignment(out BillAssignment assignment, out SplitRule? split, out string error)
    {
        assignment = EditorAssignment;
        split = null;
        error = string.Empty;
        var adults = Adults.Count > 0
            ? Adults
            : Members.Where(member => !member.IsDependant && !member.IsArchived).ToList();

        switch (assignment)
        {
            case BillAssignment.Unassigned:
            case BillAssignment.SharedAccount:
                return true;

            case BillAssignment.MemberPaysAll:
                if (EditorPayer is null)
                {
                    error = "Choose who pays this bill.";
                    return false;
                }

                split = SplitRule.SoleResponsibility(EditorPayer.Id);
                return true;

            case BillAssignment.PercentageSplit:
                if (adults.Count < 2
                    || !decimal.TryParse(EditorFirstShare, out var firstPercent)
                    || !decimal.TryParse(EditorSecondShare, out var secondPercent))
                {
                    error = "Enter a percentage for each adult.";
                    return false;
                }

                if (firstPercent < 0m || secondPercent < 0m)
                {
                    error = "Percentages cannot be negative.";
                    return false;
                }

                if (Math.Abs(firstPercent + secondPercent - 100m) > 0.001m)
                {
                    error = "Percentage splits must total exactly 100%.";
                    return false;
                }

                split = new SplitRule
                {
                    Method = SplitMethod.Percentage,
                    Participants = [adults[0].Id, adults[1].Id],
                    Percentages = [firstPercent, secondPercent]
                };
                return ValidateSplit(split, out error);

            case BillAssignment.FixedDollarSplit:
            case BillAssignment.EnteredContributions:
                if (adults.Count < 2
                    || !AmountParsing.TryParseMoney(EditorFirstShare, out var firstAmount)
                    || !AmountParsing.TryParseMoney(EditorSecondShare, out var secondAmount))
                {
                    error = "Enter each person's contribution.";
                    return false;
                }

                if (firstAmount.IsNegative || secondAmount.IsNegative)
                {
                    error = "Contributions cannot be negative.";
                    return false;
                }

                if (SelectedReservation is { } selected
                    && _session.IsOpen
                    && _session.Document.Expenses.FirstOrDefault(item => item.Id == selected.ObligationId) is { } bill)
                {
                    var total = (firstAmount + secondAmount).Round();
                    if (total != bill.ExpectedAmount.Round())
                    {
                        error =
                            $"Dollar contributions must equal the assigned bill amount of {bill.ExpectedAmount.ToDisplayString()}.";
                        return false;
                    }
                }

                split = new SplitRule
                {
                    Method = SplitMethod.FixedAmount,
                    Participants = [adults[0].Id, adults[1].Id],
                    FixedAmounts = [firstAmount, secondAmount]
                };
                return ValidateSplit(split, out error);

            default:
                error = "Choose an assignment.";
                return false;
        }
    }

    private static bool ValidateSplit(SplitRule split, out string error)
    {
        try
        {
            split.Validate();
            error = string.Empty;
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    [RelayCommand]
    private async Task AddTransferAsync(CancellationToken cancellationToken)
    {
        if (!TryBeginSave())
        {
            return;
        }

        try
        {
            if (!_session.IsOpen)
            {
                ErrorMessage = "Open the household database first.";
                return;
            }

            if (TransferFrom is not { } from || TransferTo is not { } to)
            {
                ErrorMessage = "Choose who is sending and who is receiving.";
                return;
            }

            if (from.Id == to.Id)
            {
                ErrorMessage = "A transfer needs two different people.";
                return;
            }

            if (!AmountParsing.TryParseMoney(TransferAmount, out var amount) || amount.IsZero || amount.IsNegative)
            {
                ErrorMessage = "Enter the amount being transferred.";
                return;
            }

            var date = TransferDate is { } chosen
                ? DateOnly.FromDateTime(chosen)
                : DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

            PreviewTransfer();
            if (!_dialog.Confirm(
                    "Record transfer",
                    (string.IsNullOrWhiteSpace(TransferPreview)
                        ? $"Move {amount.ToDisplayString()} from {from.Name}'s personal balance to {to.Name}'s?"
                        : TransferPreview)))
            {
                return;
            }

            var document = _session.Document;
            var transfer = new PersonalTransfer
            {
                Id = _editingTransferId ?? Guid.NewGuid(),
                FromMemberId = from.Id,
                ToMemberId = to.Id,
                Amount = amount,
                Date = date,
                Purpose = string.IsNullOrWhiteSpace(TransferPurpose) ? null : TransferPurpose.Trim(),
                IsRecurring = TransferIsRecurring,
                RecurringFrequency = TransferIsRecurring ? TransferFrequency : Frequency.OneOff
            };

            SelectedRecordId = transfer.Id;
            var transfers = document.Transfers
                .Where(item => item.Id != transfer.Id)
                .Append(transfer)
                .ToList();

            if (!_session.TryReplace(document with { Transfers = transfers }, out var error))
            {
                ErrorMessage = error;
                return;
            }

            var result = await EditorSaveCoordinator.PersistCurrentAsync(
                _session,
                cancellationToken,
                "Transfer saved",
                saved => saved.Transfers.Any(item => item.Id == transfer.Id && item.Amount == amount));
            ErrorMessage = result.IsSuccess ? null : result.Message;
            StatusMessage = result.IsSuccess ? result.Message : StatusMessage;
            if (result.IsSuccess)
            {
                DismissEditor();
            }
        }
        finally
        {
            EndSave();
        }
    }

    [RelayCommand]
    private async Task RemoveTransferAsync(TransferRow? row, CancellationToken cancellationToken)
    {
        if (row is null || !_session.IsOpen)
        {
            return;
        }

        if (!_dialog.Confirm("Remove transfer", $"Remove the transfer of {row.Amount} on {row.Date}?"))
        {
            return;
        }

        var document = _session.Document;
        var transfers = document.Transfers.Where(transfer => transfer.Id != row.Id).ToList();

        if (!_session.TryReplace(document with { Transfers = transfers }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Transfer removed."
            : _session.LastError ?? "The transfer could not be removed.";
    }

    private bool TryBeginSave()
    {
        if (System.Threading.Interlocked.Exchange(ref _saveDepth, 1) != 0)
        {
            return false;
        }

        return true;
    }

    private void EndSave() => System.Threading.Interlocked.Exchange(ref _saveDepth, 0);

    private void RebuildPayerChoices()
    {
        var choices = new List<PayerChoice>
        {
            new("unassigned", "Unassigned", BillAssignment.Unassigned, null)
        };

        foreach (var adult in Adults)
        {
            choices.Add(new($"member:{adult.Id}", $"{adult.Name} pays all", BillAssignment.MemberPaysAll, adult.Id));
        }

        choices.Add(new("shared", "Shared household", BillAssignment.PercentageSplit, null));
        choices.Add(new("account", "Shared account", BillAssignment.SharedAccount, null));
        PayerChoices = choices;
        OnPropertyChanged(nameof(PayerChoices));
        OnPropertyChanged(nameof(FirstAdultShareLabel));
        OnPropertyChanged(nameof(SecondAdultShareLabel));
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Reservations = [];
            Shares = [];
            Transfers = [];
            Members = [];
            Adults = [];
            SplitWarnings = [];
            SplitSummary = string.Empty;
            OverrideSummary = string.Empty;
            RoundingSummary = string.Empty;
            Totals = string.Empty;
            ReserveAmount = string.Empty;
            TransferAmount = string.Empty;
            TransferPurpose = string.Empty;
            TransferFrom = null;
            TransferTo = null;
            TransferDate = null;
            SelectedReservation = null;
            SelectedRecordId = null;
            StatusMessage = null;
            ErrorMessage = null;
            DismissEditor();
            RebuildPayerChoices();
            Notify();
            return;
        }

        var document = _session.Document;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var allocation = PaychequeAllocator.Allocate(document, today);

        Members = document.Members;
        Adults = document.Members
            .Where(member => !member.IsDependant && !member.IsArchived)
            .ToList();
        RebuildPayerChoices();

        var register = ObligationRegister.Build(document, today);

        Reservations = register.Lines
            .Select(line => new ReservationRow(
                line.Id,
                line.Kind,
                line.Name,
                line.Owner,
                line.Classification,
                line.Category,
                line.Priority,
                line.Frequency,
                line.DueDateText,
                line.AmountRequired.ToDisplayString(),
                line.AmountPaid.ToDisplayString(),
                line.AmountReserved.ToDisplayString(),
                line.StillRequired.ToDisplayString(),
                line.RequiredFromNextPaycheque.ToDisplayString(),
                line.PaydaysRemaining.ToString(System.Globalization.CultureInfo.InvariantCulture),
                line.StatusText,
                line.Classification,
                line.Consequence,
                line.AttentionText,
                line.IsUnassigned,
                line.Id == SelectedRecordId))
            .ToList();

        var selectedId = SelectedReservation?.ObligationId ?? SelectedRecordId;
        SelectedReservation = selectedId is { } id
            ? Reservations.FirstOrDefault(item => item.ObligationId == id)
            : null;

        if (!IsEditorOpen)
        {
            AssignmentPreview = string.Empty;
            TransferPreview = string.Empty;
        }

        var chosen = allocation.SharedSplit;
        var fallback = allocation.DefaultSplit ?? chosen;

        Shares = chosen.Shares
            .Select(share =>
            {
                var defaultShare = fallback.Shares.FirstOrDefault(item => item.MemberId == share.MemberId);
                var defaultAmount = defaultShare?.Amount ?? share.Amount;
                var difference = (share.Amount - defaultAmount).Round();

                return new SplitRow(
                    share.MemberName,
                    $"{share.Percent:0.##}%",
                    share.Amount.ToDisplayString(),
                    defaultAmount.ToDisplayString(),
                    difference.IsZero
                        ? "Same as the proportional result"
                        : $"{difference.ToDisplayString()} away from the proportional result",
                    share.CarriesRoundingRemainder
                        ? $"Carries the {share.RoundingRemainder.Abs().ToDisplayString()} rounding remainder"
                        : string.Empty);
            })
            .ToList();

        SplitSummary = chosen.Explanation;

        OverrideSummary = allocation.DefaultSplit is null
            ? "The proportional result is being used, with no override in place."
            : $"An override is in place. The proportional result was {fallback.Explanation}";

        RoundingSummary = AllocationReconciler.DescribeRounding(chosen)
                          + (chosen.ReconcilesExactly
                              ? " The shares add back to the total exactly."
                              : " The shares do not add back to the total, which is a fault.");

        SplitWarnings = chosen.Warnings;

        Totals =
            $"Due before the next payday: {allocation.Reservations.TotalDueNow.ToDisplayString()}. " +
            $"To reserve from this paycheque: {allocation.Reservations.TotalToReserve.ToDisplayString()}. " +
            $"Unfunded: {allocation.Reservations.TotalShortfall.ToDisplayString()}.";

        Transfers = document.Transfers
            .OrderByDescending(transfer => transfer.Date)
            .Select(transfer => new TransferRow(
                transfer.Id,
                document.MemberName(transfer.FromMemberId),
                document.MemberName(transfer.ToMemberId),
                transfer.Amount.ToDisplayString(),
                transfer.Date.ToString("yyyy-MM-dd"),
                transfer.Purpose ?? "No note recorded",
                transfer.IsRecurring
                    ? transfer.RecurringFrequency.ToDisplayName()
                    : "One-time",
                transfer.Id == SelectedRecordId))
            .ToList();

        Notify();
    }

    private void RematchSelection()
    {
        Reservations = Reservations
            .Select(item => item with { IsSelected = item.ObligationId == SelectedRecordId })
            .ToList();
        Transfers = Transfers
            .Select(item => item with { IsSelected = item.Id == SelectedRecordId })
            .ToList();
        SelectedReservation = Reservations.FirstOrDefault(item => item.ObligationId == SelectedRecordId)
                              ?? SelectedReservation;
        OnPropertyChanged(nameof(Reservations));
        OnPropertyChanged(nameof(Transfers));
        OnPropertyChanged(nameof(HasReservations));
        OnPropertyChanged(nameof(HasTransfers));
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Reservations));
        OnPropertyChanged(nameof(HasReservations));
        OnPropertyChanged(nameof(HasTransfers));
        OnPropertyChanged(nameof(Shares));
        OnPropertyChanged(nameof(Transfers));
        OnPropertyChanged(nameof(Members));
        OnPropertyChanged(nameof(Adults));
        OnPropertyChanged(nameof(SplitWarnings));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(IsSharedHousehold));
        OnPropertyChanged(nameof(FirstAdultShareLabel));
        OnPropertyChanged(nameof(SecondAdultShareLabel));
    }
}
