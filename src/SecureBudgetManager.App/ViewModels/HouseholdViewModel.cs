using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.App.ViewModels;

public sealed record MemberListItem(
    Guid Id,
    string Name,
    string Role,
    string Designation,
    bool IsArchived)
{
    public string Summary => IsArchived
        ? $"{Name} · {Role} · {Designation} · Archived"
        : $"{Name} · {Role} · {Designation}";
}

public sealed partial class HouseholdViewModel : PageViewModel, IEditablePage
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private Guid? _editingId;
    private bool _suppressNameSync;

    public HouseholdViewModel(IBudgetSession session, IUserDialog dialog)
        : base(
            "Household",
            "Setup",
            "Household members stay on this computer in the local database. Names are enough — do not enter Social Security numbers or bank details.")
    {
        _session = session;
        _dialog = dialog;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public IReadOnlyList<MemberListItem> Members { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMembers))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private int memberCount;

    public bool HasMembers => MemberCount > 0;

    public bool ShowEmptyState => _session.IsOpen && MemberCount == 0 && !IsEditorOpen;

    [ObservableProperty]
    private string householdName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveHouseholdCommand))]
    private bool householdNameDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyCanExecuteChangedFor(nameof(SaveMemberCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelEditorCommand))]
    private bool isEditorOpen;

    [ObservableProperty]
    private string editorTitle = "Add member";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveMemberCommand))]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private string editorName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private bool editorIsDependant;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string EditorSaveLabel => "Save member";

    public string? EditorEffectPreview =>
        EditorIsDependant
            ? "This person will stay in shared household needs such as food and housing."
            : "This person can have their own income and assigned bills.";

    public bool HasEditorError => HasError;

    public string? EditorError => ErrorMessage;

    public ICommand SaveEditorCommand => SaveMemberCommand;

    ICommand IEditablePage.CancelEditorCommand => CancelEditorCommand;

    public bool TryLeaveEditor() => ConfirmDiscardEditor();

    public void DismissEditor() => CloseEditor();

    public bool HasEditorChanges => IsEditorOpen && (
        !string.Equals(EditorName, _originalEditorName, StringComparison.Ordinal)
        || EditorIsDependant != _originalIsDependant
        || EditorIsDiscretionaryEligible != _originalIsDiscretionaryEligible
        || EditorParticipatesInSharedCosts != _originalParticipatesInSharedCosts
        || EditorIsArchived != _originalIsArchived
        || !string.Equals(EditorNotes, _originalNotes, StringComparison.Ordinal));

    private string _originalEditorName = string.Empty;
    private bool _originalIsDependant;
    private bool _originalIsDiscretionaryEligible;
    private bool _originalParticipatesInSharedCosts;
    private bool _originalIsArchived;
    private string _originalNotes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private bool editorIsDiscretionaryEligible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private bool editorParticipatesInSharedCosts = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private bool editorIsArchived;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private string editorNotes = string.Empty;

    partial void OnHouseholdNameChanged(string value)
    {
        if (_suppressNameSync || !_session.IsOpen)
        {
            return;
        }

        HouseholdNameDirty = !string.Equals(
            value.Trim(),
            _session.Document.HouseholdName,
            StringComparison.Ordinal);
    }

    partial void OnEditorNameChanged(string value)
    {
        OnPropertyChanged(nameof(HasEditorChanges));
        SaveMemberCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BeginAdd()
    {
        if (!ConfirmDiscardEditor())
        {
            return;
        }

        ErrorMessage = null;
        _editingId = null;
        EditorTitle = "Add member";
        EditorName = string.Empty;
        EditorIsDependant = false;
        EditorIsDiscretionaryEligible = false;
        EditorParticipatesInSharedCosts = true;
        EditorIsArchived = false;
        EditorNotes = string.Empty;
        _originalEditorName = string.Empty;
        _originalIsDependant = false;
        _originalIsDiscretionaryEligible = false;
        _originalParticipatesInSharedCosts = true;
        _originalIsArchived = false;
        _originalNotes = string.Empty;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasEditorChanges));
    }

    [RelayCommand]
    private void BeginEdit(MemberListItem? item)
    {
        if (item is null || !ConfirmDiscardEditor())
        {
            return;
        }

        var member = _session.Document.Members.FirstOrDefault(entry => entry.Id == item.Id);
        if (member is null)
        {
            return;
        }

        ErrorMessage = null;
        _editingId = member.Id;
        EditorTitle = "Edit member";
        EditorName = member.Name;
        EditorIsDependant = member.IsDependant;
        EditorIsDiscretionaryEligible = member.IsDiscretionaryEligible;
        EditorParticipatesInSharedCosts = member.ParticipatesInSharedCosts;
        EditorIsArchived = member.IsArchived;
        EditorNotes = member.Notes ?? string.Empty;
        _originalEditorName = member.Name;
        _originalIsDependant = member.IsDependant;
        _originalIsDiscretionaryEligible = member.IsDiscretionaryEligible;
        _originalParticipatesInSharedCosts = member.ParticipatesInSharedCosts;
        _originalIsArchived = member.IsArchived;
        _originalNotes = member.Notes ?? string.Empty;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(HasEditorChanges));
    }

    [RelayCommand(CanExecute = nameof(CanSaveMember))]
    private async Task SaveMemberAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = ValidateEditor();
        if (ErrorMessage is not null)
        {
            return;
        }

        var name = EditorName.Trim();
        var document = _session.Document;
        var members = document.Members.ToList();

        if (EditorIsDiscretionaryEligible != _originalIsDiscretionaryEligible
            && !_dialog.Confirm(
                EditorIsDiscretionaryEligible
                    ? "Grant personal discretionary allocation"
                    : "Remove personal discretionary allocation",
                EditorIsDiscretionaryEligible
                    ? $"Give {name} a personal discretionary envelope calculated from their own income? This is never inferred from membership alone."
                    : $"Remove {name}'s personal discretionary envelope? Shared household needs are unchanged."))
        {
            return;
        }

        if (_editingId is { } id)
        {
            var index = members.FindIndex(member => member.Id == id);
            if (index < 0)
            {
                ErrorMessage = "That member is no longer in the household.";
                return;
            }

            members[index] = members[index] with
            {
                Name = name,
                IsDependant = EditorIsDependant,
                IsDiscretionaryEligible = EditorIsDiscretionaryEligible,
                ParticipatesInSharedCosts = EditorParticipatesInSharedCosts,
                IsArchived = EditorIsArchived,
                Notes = string.IsNullOrWhiteSpace(EditorNotes) ? null : EditorNotes.Trim()
            };
        }
        else
        {
            members.Add(new HouseholdMember
            {
                Id = Guid.NewGuid(),
                Name = name,
                IsDependant = EditorIsDependant,
                IsDiscretionaryEligible = EditorIsDiscretionaryEligible,
                ParticipatesInSharedCosts = EditorIsDependant ? false : EditorParticipatesInSharedCosts,
                IsArchived = EditorIsArchived,
                Notes = string.IsNullOrWhiteSpace(EditorNotes) ? null : EditorNotes.Trim()
            });
        }

        var householdName = string.IsNullOrWhiteSpace(HouseholdName)
            ? document.HouseholdName
            : HouseholdName.Trim();

        if (!await CommitAsync(document with { HouseholdName = householdName, Members = members }, cancellationToken))
        {
            return;
        }

        CloseEditor();
        StatusMessage = "Member saved.";
    }

    private bool CanSaveMember() => _session.IsOpen && !_session.IsSaving && IsEditorOpen;

    [RelayCommand(CanExecute = nameof(CanCancelEditor))]
    private void CancelEditor()
    {
        if (!ConfirmDiscardEditor(forcePrompt: HasEditorChanges))
        {
            return;
        }

        CloseEditor();
    }

    private bool CanCancelEditor() => IsEditorOpen;

    [RelayCommand]
    private async Task RemoveAsync(MemberListItem? item, CancellationToken cancellationToken)
    {
        if (item is null || !_session.IsOpen)
        {
            return;
        }

        if (IsEditorOpen && _editingId == item.Id && !ConfirmDiscardEditor(forcePrompt: true))
        {
            return;
        }

        if (!_dialog.Confirm(
                "Remove member",
                $"Remove {item.Name} from the household? Income, expenses and benefits that still belong to them must be removed first."))
        {
            return;
        }

        var document = _session.Document;
        if (document.IncomeSources.Any(source => source.MemberId == item.Id)
            || document.PayrollProfiles.Any(profile => profile.MemberId == item.Id)
            || document.Benefits.Any(benefit => benefit.MemberId == item.Id)
            || document.Expenses.Any(expense => expense.Split is { } split && split.Participants.Contains(item.Id)))
        {
            ErrorMessage = "Remove this member's income, payroll, benefits and expenses first.";
            return;
        }

        var remaining = document.Members.Where(member => member.Id != item.Id).ToList();
        var rules = document.Rules;
        var customPercents = rules.CustomDiscretionaryPercents
            .Where(pair => pair.Key != item.Id)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var overrides = rules.SharedContributionOverrides
            .Where(pair => pair.Key != item.Id)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        var trimmed = document with
        {
            Members = remaining,
            Transfers = document.Transfers
                .Where(transfer => transfer.FromMemberId != item.Id && transfer.ToMemberId != item.Id)
                .ToList(),
            Transactions = document.Transactions
                .Select(transaction => transaction.SpentByMemberId == item.Id
                    ? transaction with { SpentByMemberId = null }
                    : transaction)
                .ToList(),
            Rules = rules with
            {
                CustomDiscretionaryPercents = customPercents,
                SharedContributionOverrides = overrides,
                RoundingRemainderMemberId = rules.RoundingRemainderMemberId == item.Id
                    ? null
                    : rules.RoundingRemainderMemberId
            }
        };

        if (!await CommitAsync(trimmed, cancellationToken))
        {
            return;
        }

        if (_editingId == item.Id)
        {
            CloseEditor();
        }

        StatusMessage = "Member removed.";
    }

    [RelayCommand]
    private async Task ArchiveAsync(MemberListItem? item, CancellationToken cancellationToken)
    {
        if (item is null || !_session.IsOpen)
        {
            return;
        }

        var document = _session.Document;
        var members = document.Members.ToList();
        var index = members.FindIndex(member => member.Id == item.Id);
        if (index < 0)
        {
            return;
        }

        var member = members[index];
        if (!member.IsArchived
            && document.Members.Count(entry => !entry.IsDependant && !entry.IsArchived && entry.Id != member.Id) == 0
            && !member.IsDependant)
        {
            ErrorMessage = "A household needs at least one active adult member.";
            return;
        }

        var nextState = !member.IsArchived;
        if (!_dialog.Confirm(
                nextState ? "Archive member" : "Reactivate member",
                nextState
                    ? $"{member.Name} will stay in history. New allocations will ignore them. Continue?"
                    : $"Return {member.Name} to the active household?"))
        {
            return;
        }

        members[index] = member with { IsArchived = nextState };
        if (!await CommitAsync(document with { Members = members }, cancellationToken))
        {
            return;
        }

        StatusMessage = nextState ? "Member archived. Historical records remain." : "Member reactivated.";
    }

    [RelayCommand(CanExecute = nameof(CanSaveHousehold))]
    private async Task SaveHouseholdAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = ValidateHouseholdName(HouseholdName, _session.Document.Members.Count);
        if (ErrorMessage is not null)
        {
            return;
        }

        var next = _session.Document with { HouseholdName = HouseholdName.Trim() };
        if (!await CommitAsync(next, cancellationToken))
        {
            return;
        }

        HouseholdNameDirty = false;
        StatusMessage = "Household saved.";
    }

    private bool CanSaveHousehold() => _session.IsOpen && !_session.IsSaving && HouseholdNameDirty;

    public string? ValidateEditor()
    {
        if (string.IsNullOrWhiteSpace(EditorName))
        {
            return "A household member needs a name.";
        }

        var document = _session.Document;
        var otherNames = document.Members
            .Where(member => member.Id != _editingId)
            .Select(member => member.Name);

        if (otherNames.Any(name => string.Equals(name, EditorName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return "That name is already used by another member.";
        }

        if (EditorIsDependant && document.Members.Count(member => !member.IsDependant && member.Id != _editingId) == 0)
        {
            return "A household needs at least one adult member.";
        }

        return ValidateHouseholdName(HouseholdName, document.Members.Count + (_editingId is null ? 1 : 0));
    }

    public static string? ValidateHouseholdName(string? name, int memberCount)
    {
        if (string.IsNullOrWhiteSpace(name) && memberCount > 0)
        {
            return "A household needs a name.";
        }

        return null;
    }

    private async Task<bool> CommitAsync(BudgetDocument document, CancellationToken cancellationToken)
    {
        ErrorMessage = null;

        if (!_session.TryReplace(document, out var error))
        {
            ErrorMessage = error;
            return false;
        }

        var saved = await _session.SaveAsync(cancellationToken);
        if (!saved)
        {
            ErrorMessage = _session.LastError ?? "The household data could not be saved.";
            return false;
        }

        return true;
    }

    private bool ConfirmDiscardEditor(bool forcePrompt = false)
    {
        if (!IsEditorOpen)
        {
            return true;
        }

        if (!HasEditorChanges && !forcePrompt)
        {
            CloseEditor();
            return true;
        }

        if (!_dialog.Confirm("Unsaved changes", "Discard the member you are editing?"))
        {
            return false;
        }

        CloseEditor();
        return true;
    }

    private void CloseEditor()
    {
        IsEditorOpen = false;
        EditorName = string.Empty;
        EditorIsDependant = false;
        EditorIsDiscretionaryEligible = false;
        EditorParticipatesInSharedCosts = true;
        EditorIsArchived = false;
        EditorNotes = string.Empty;
        _editingId = null;
        _originalEditorName = string.Empty;
        _originalIsDependant = false;
        _originalIsDiscretionaryEligible = false;
        _originalParticipatesInSharedCosts = true;
        _originalIsArchived = false;
        _originalNotes = string.Empty;
        ErrorMessage = null;
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasEditorChanges));
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            _suppressNameSync = true;
            HouseholdName = string.Empty;
            HouseholdNameDirty = false;
            Members = [];
            MemberCount = 0;
            CloseEditor();
            StatusMessage = null;
            ErrorMessage = null;
            _suppressNameSync = false;
            OnPropertyChanged(nameof(Members));
            return;
        }

        var document = _session.Document;
        _suppressNameSync = true;
        HouseholdName = document.HouseholdName;
        HouseholdNameDirty = false;
        _suppressNameSync = false;

        Members = document.Members
            .Select(member => new MemberListItem(
                member.Id,
                member.Name,
                member.IsDependant ? "Dependant" : "Adult",
                member.IsDiscretionaryEligible
                    ? "Eligible for personal discretionary"
                    : "No personal discretionary allocation",
                member.IsArchived))
            .ToList();
        MemberCount = Members.Count;
        OnPropertyChanged(nameof(Members));
        SaveHouseholdCommand.NotifyCanExecuteChanged();
        SaveMemberCommand.NotifyCanExecuteChanged();
    }
}
