using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Guidance;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.ViewModels;

public sealed record ReservationRow(
    Guid ObligationId,
    ObligationKind Kind,
    string Name,
    string DueDate,
    string AmountDue,
    string AlreadyReserved,
    string PaydaysRemaining,
    string RequiredNow,
    string Status,
    string Tier,
    string Explanation);

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
    string Recurrence);

/// <summary>
/// Where the household sets aside money against named obligations, sees how shared costs are
/// divided, and records any deliberate transfer between two people's personal balances.
/// </summary>
public sealed partial class AllocationsViewModel : PageViewModel
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;

    public AllocationsViewModel(IBudgetSession session, IUserDialog dialog, TimeProvider clock)
        : base(
            "Allocations",
            "Reservations",
            "What each future obligation needs from the paycheque in hand, how shared costs are " +
            "divided between earners, and what has already been set aside.")
    {
        _session = session;
        _dialog = dialog;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<Frequency>> TransferFrequencies { get; } = FrequencyChoices.Recurring;

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

    public IReadOnlyList<ReservationRow> Reservations { get; private set; } = [];

    public IReadOnlyList<SplitRow> Shares { get; private set; } = [];

    public IReadOnlyList<TransferRow> Transfers { get; private set; } = [];

    public IReadOnlyList<HouseholdMember> Members { get; private set; } = [];

    public IReadOnlyList<string> SplitWarnings { get; private set; } = [];

    [RelayCommand]
    private async Task SaveReserveAsync(CancellationToken cancellationToken)
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

        ErrorMessage = null;
        ReserveAmount = string.Empty;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? $"{amount.ToDisplayString()} recorded against {selected.Name}."
            : _session.LastError ?? "The reserve could not be saved.";
    }

    [RelayCommand]
    private async Task AddTransferAsync(CancellationToken cancellationToken)
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

        if (!_dialog.Confirm(
                "Record transfer",
                $"Move {amount.ToDisplayString()} from {from.Name}'s personal balance to {to.Name}'s?"))
        {
            return;
        }

        var document = _session.Document;
        var transfers = document.Transfers.ToList();

        transfers.Add(new PersonalTransfer
        {
            Id = Guid.NewGuid(),
            FromMemberId = from.Id,
            ToMemberId = to.Id,
            Amount = amount,
            Date = date,
            Purpose = string.IsNullOrWhiteSpace(TransferPurpose) ? null : TransferPurpose.Trim(),
            IsRecurring = TransferIsRecurring,
            RecurringFrequency = TransferIsRecurring ? TransferFrequency : Frequency.OneOff
        });

        if (!_session.TryReplace(document with { Transfers = transfers }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        TransferAmount = string.Empty;
        TransferPurpose = string.Empty;

        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? $"{amount.ToDisplayString()} recorded from {from.Name} to {to.Name}."
            : _session.LastError ?? "The transfer could not be saved.";
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

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Reservations = [];
            Shares = [];
            Transfers = [];
            Members = [];
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
            StatusMessage = null;
            ErrorMessage = null;
            Notify();
            return;
        }

        var document = _session.Document;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var allocation = PaychequeAllocator.Allocate(document, today);

        Members = document.Members;

        Reservations = allocation.Reservations.Lines
            .Select(line => new ReservationRow(
                line.Obligation.Id,
                line.Obligation.Kind,
                line.Name,
                line.DueDate.ToString("yyyy-MM-dd"),
                line.AmountDue.ToDisplayString(),
                line.AlreadyReserved.ToDisplayString(),
                line.PaydaysRemaining.ToString(),
                line.RequiredFromThisPaycheque.ToDisplayString(),
                line.Status.ToDisplayName(),
                line.Tier.ToDisplayName(),
                line.Explanation))
            .ToList();

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
                    : "One-time"))
            .ToList();

        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Reservations));
        OnPropertyChanged(nameof(Shares));
        OnPropertyChanged(nameof(Transfers));
        OnPropertyChanged(nameof(Members));
        OnPropertyChanged(nameof(SplitWarnings));
    }
}
