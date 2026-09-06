using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Income;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.ViewModels;

public enum IncomeKindSelection
{
    Hourly = 0,
    Salary = 1,
    FixedRecurring = 2,
    Mileage = 3
}

public sealed record IncomeListItem(
    Guid Id,
    string Name,
    string OwnerName,
    string Kind,
    string Frequency,
    string Weekly,
    string Monthly,
    string Annual,
    string Status,
    bool IsMileage);

public sealed partial class IncomeViewModel : PageViewModel, IEditablePage
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private Guid? _editingId;
    private string _originalFingerprint = string.Empty;

    public IncomeViewModel(IBudgetSession session, IUserDialog dialog)
        : base(
            "Income",
            "Money in",
            "Pay and reimbursements are stored only in the local database. Mileage reimbursement is kept separate from taxable earnings.")
    {
        _session = session;
        _dialog = dialog;
        _session.Changed += OnSessionChanged;
        KindOptions =
        [
            new ChoiceOption<IncomeKindSelection>(IncomeKindSelection.Hourly, "Hourly"),
            new ChoiceOption<IncomeKindSelection>(IncomeKindSelection.Salary, "Salary"),
            new ChoiceOption<IncomeKindSelection>(IncomeKindSelection.FixedRecurring, "Fixed recurring"),
            new ChoiceOption<IncomeKindSelection>(IncomeKindSelection.Mileage, "Mileage reimbursement")
        ];
        FrequencyOptions = FrequencyChoices.Income;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<IncomeKindSelection>> KindOptions { get; }

    public IReadOnlyList<ChoiceOption<Frequency>> FrequencyOptions { get; }

    public IReadOnlyList<ChoiceOption<Guid>> OwnerOptions { get; private set; } = [];

    public IReadOnlyList<IncomeListItem> Items { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(CannotAddReason))]
    private int itemCount;

    public bool HasItems => ItemCount > 0;

    public bool CanAdd => _session.IsOpen && OwnerOptions.Count > 0;

    private bool CanBeginAdd() => CanAdd;

    public string? CannotAddReason =>
        !_session.IsOpen ? null
        : OwnerOptions.Count == 0 ? "Add a household member before recording income."
        : null;

    public bool ShowEmptyState => _session.IsOpen && ItemCount == 0 && !IsEditorOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyCanExecuteChangedFor(nameof(SaveEntryCommand))]
    private bool isEditorOpen;

    [ObservableProperty]
    private string editorTitle = "Add income";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private string editorName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private Guid editorOwnerId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHourly))]
    [NotifyPropertyChangedFor(nameof(IsSalary))]
    [NotifyPropertyChangedFor(nameof(IsFixedRecurring))]
    [NotifyPropertyChangedFor(nameof(IsMileage))]
    [NotifyPropertyChangedFor(nameof(ShowTaxable))]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private IncomeKindSelection editorKind = IncomeKindSelection.Hourly;

    public bool IsHourly => EditorKind == IncomeKindSelection.Hourly;

    public bool IsSalary => EditorKind == IncomeKindSelection.Salary;

    public bool IsFixedRecurring => EditorKind == IncomeKindSelection.FixedRecurring;

    public bool IsMileage => EditorKind == IncomeKindSelection.Mileage;

    public bool ShowTaxable => !IsMileage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private Frequency editorFrequency = Frequency.Weekly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorHourlyRate = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorHoursConservative = "35";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorHoursNormal = "37.5";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorHoursOptimistic = "40";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorOvertimeMultiplier = "1.5";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorOvertimeConservative = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorOvertimeNormal = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorOvertimeOptimistic = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorAnnualSalary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorPeriodAmount = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorMiles = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    [NotifyPropertyChangedFor(nameof(EquivalentSummary))]
    private string editorRatePerMile = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private bool editorIsTaxable = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private bool editorIsReimbursement;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private DateTime? editorStartDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private DateTime? editorEmploymentStartDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorChanges))]
    private bool editorIsActive = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string EditorSaveLabel => "Save income";

    public string? EditorEffectPreview => EquivalentSummary;

    public bool HasEditorError => HasError;

    public string? EditorError => ErrorMessage;

    public ICommand SaveEditorCommand => SaveEntryCommand;

    ICommand IEditablePage.CancelEditorCommand => CancelEditorCommand;

    public bool TryLeaveEditor() => ConfirmDiscardEditor();

    public void DismissEditor() => CloseEditor();

    public bool HasEditorChanges => IsEditorOpen && Fingerprint() != _originalFingerprint;

    public string EquivalentSummary
    {
        get
        {
            if (!TryBuildSource(Guid.NewGuid(), out var source, out _))
            {
                return "Enter a valid amount to see weekly, average-monthly and annual equivalents.";
            }

            var annual = source.GrossPerYear(IncomeEstimate.Normal).Round();
            var weekly = BudgetOverviewCalculator.ToPeriod(annual, DisplayPeriod.Weekly);
            var monthly = BudgetOverviewCalculator.ToPeriod(annual, DisplayPeriod.AverageMonthly);
            return $"Expected: {weekly.ToDisplayString()} weekly · {monthly.ToDisplayString()} average monthly · {annual.ToDisplayString()} a year.";
        }
    }

    partial void OnEditorIsReimbursementChanged(bool value)
    {
        if (value)
        {
            EditorIsTaxable = false;
        }
    }

    partial void OnEditorKindChanged(IncomeKindSelection value)
    {
        if (value == IncomeKindSelection.Mileage)
        {
            EditorIsTaxable = false;
            EditorIsReimbursement = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBeginAdd))]
    private void BeginAdd()
    {
        if (!ConfirmDiscardEditor())
        {
            return;
        }

        OpenEditor(null, "Add income", null);
    }

    [RelayCommand]
    private void BeginEdit(IncomeListItem? item)
    {
        if (item is null || !ConfirmDiscardEditor())
        {
            return;
        }

        var source = _session.Document.IncomeSources.FirstOrDefault(entry => entry.Id == item.Id);
        if (source is null)
        {
            return;
        }

        OpenEditor(source.Id, "Edit income", source);
    }

    [RelayCommand]
    private void Duplicate(IncomeListItem? item)
    {
        if (item is null || !ConfirmDiscardEditor())
        {
            return;
        }

        var source = _session.Document.IncomeSources.FirstOrDefault(entry => entry.Id == item.Id);
        if (source is null)
        {
            return;
        }

        OpenEditor(null, "Duplicate income", source with { Id = Guid.NewGuid(), Name = source.Name + " (copy)" });
    }

    [RelayCommand(CanExecute = nameof(CanSaveEntry))]
    private async Task SaveEntryAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildSource(_editingId ?? Guid.NewGuid(), out var source, out var error))
        {
            ErrorMessage = error;
            return;
        }

        var document = _session.Document;
        var sources = document.IncomeSources.ToList();

        if (_editingId is { } id)
        {
            var index = sources.FindIndex(entry => entry.Id == id);
            if (index < 0)
            {
                ErrorMessage = "That income record is no longer available.";
                return;
            }

            sources[index] = source with { Id = id };
        }
        else
        {
            sources.Add(source);
        }

        if (!await CommitAsync(document with { IncomeSources = sources }, cancellationToken))
        {
            return;
        }

        CloseEditor();
        StatusMessage = "Income saved.";
    }

    private bool CanSaveEntry() => _session.IsOpen && !_session.IsSaving && IsEditorOpen;

    [RelayCommand]
    private void CancelEditor()
    {
        if (!ConfirmDiscardEditor(forcePrompt: HasEditorChanges))
        {
            return;
        }

        CloseEditor();
    }

    [RelayCommand]
    private async Task RemoveAsync(IncomeListItem? item, CancellationToken cancellationToken)
    {
        if (item is null || !_session.IsOpen)
        {
            return;
        }

        if (!_dialog.Confirm("Remove income", $"Remove the income record named {item.Name}?"))
        {
            return;
        }

        var document = _session.Document;
        var remaining = document.IncomeSources.Where(source => source.Id != item.Id).ToList();
        var payslips = document.Payslips.Where(payslip => payslip.IncomeSourceId != item.Id).ToList();

        if (!await CommitAsync(document with { IncomeSources = remaining, Payslips = payslips }, cancellationToken))
        {
            return;
        }

        if (_editingId == item.Id)
        {
            CloseEditor();
        }

        StatusMessage = "Income removed.";
    }

    public string? ValidateEditor()
    {
        TryBuildSource(_editingId ?? Guid.NewGuid(), out _, out var error);
        return error;
    }

    private bool TryBuildSource(Guid id, out IncomeSource source, out string? error)
    {
        source = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(EditorName))
        {
            error = "An income source needs a name.";
            return false;
        }

        if (EditorOwnerId == Guid.Empty)
        {
            error = "Choose who earns this income.";
            return false;
        }

        if (EditorStartDate is null)
        {
            error = "A start date is required. This is the known payday used to anchor the calendar.";
            return false;
        }

        var isTaxable = IsMileage ? false : (EditorIsReimbursement ? false : EditorIsTaxable);
        var anchor = DateOnly.FromDateTime(EditorStartDate.Value);

        try
        {
            source = EditorKind switch
            {
                IncomeKindSelection.Hourly => BuildHourly(id, isTaxable, anchor),
                IncomeKindSelection.Salary => BuildSalary(id, isTaxable, anchor),
                IncomeKindSelection.FixedRecurring => BuildFixed(id, isTaxable, anchor),
                IncomeKindSelection.Mileage => BuildMileage(id, anchor),
                _ => throw new ArgumentOutOfRangeException()
            };

            source.Validate();
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private HourlyIncome BuildHourly(Guid id, bool isTaxable, DateOnly anchor)
    {
        if (!AmountParsing.TryParseMoney(EditorHourlyRate, out var rate) || rate.IsNegative)
        {
            throw new ArgumentException("Enter a valid hourly rate.");
        }

        if (!AmountParsing.TryParseDecimal(EditorHoursConservative, out var low)
            || !AmountParsing.TryParseDecimal(EditorHoursNormal, out var mid)
            || !AmountParsing.TryParseDecimal(EditorHoursOptimistic, out var high))
        {
            throw new ArgumentException("Enter conservative, normal and optimistic hours.");
        }

        if (!AmountParsing.TryParseDecimal(EditorOvertimeMultiplier, out var multiplier))
        {
            throw new ArgumentException("Enter a valid overtime rate multiplier.");
        }

        AmountParsing.TryParseDecimal(EditorOvertimeConservative, out var otLow);
        AmountParsing.TryParseDecimal(EditorOvertimeNormal, out var otMid);
        AmountParsing.TryParseDecimal(EditorOvertimeOptimistic, out var otHigh);
        var meta = ExistingIncomeMeta(id);

        return new HourlyIncome
        {
            Id = id,
            Name = EditorName.Trim(),
            MemberId = EditorOwnerId,
            PayFrequency = EditorFrequency,
            AnchorPayDate = anchor,
            IsTaxable = isTaxable,
            IsActive = EditorIsActive,
            PayScheduleConfirmed = meta.Confirmed,
            StartsOn = EditorEmploymentStartDate is { } start
                ? DateOnly.FromDateTime(start)
                : meta.StartsOn,
            EndsOn = meta.EndsOn,
            Role = meta.Role,
            Notes = meta.Notes,
            HourlyRate = rate,
            WeeklyHours = new VariableHours(low, mid, high),
            WeeklyOvertimeHours = new VariableHours(otLow, otMid, otHigh),
            OvertimeMultiplier = multiplier
        };
    }

    private SalaryIncome BuildSalary(Guid id, bool isTaxable, DateOnly anchor)
    {
        if (!AmountParsing.TryParseMoney(EditorAnnualSalary, out var salary) || salary.IsNegative)
        {
            throw new ArgumentException("Enter a valid annual salary.");
        }

        return new SalaryIncome
        {
            Id = id,
            Name = EditorName.Trim(),
            MemberId = EditorOwnerId,
            PayFrequency = EditorFrequency,
            AnchorPayDate = anchor,
            IsTaxable = isTaxable,
            IsActive = EditorIsActive,
            PayScheduleConfirmed = ExistingIncomeMeta(id).Confirmed,
            StartsOn = EditorEmploymentStartDate is { } start
                ? DateOnly.FromDateTime(start)
                : ExistingIncomeMeta(id).StartsOn,
            EndsOn = ExistingIncomeMeta(id).EndsOn,
            Role = ExistingIncomeMeta(id).Role,
            Notes = ExistingIncomeMeta(id).Notes,
            AnnualSalary = salary
        };
    }

    private VariableIncome BuildFixed(Guid id, bool isTaxable, DateOnly anchor)
    {
        if (!AmountParsing.TryParseDecimal(EditorPeriodAmount, out var amount) || amount < 0m)
        {
            throw new ArgumentException("Enter a valid amount for each pay period.");
        }

        return new VariableIncome
        {
            Id = id,
            Name = EditorName.Trim(),
            MemberId = EditorOwnerId,
            PayFrequency = EditorFrequency,
            AnchorPayDate = anchor,
            IsTaxable = isTaxable,
            IsActive = EditorIsActive,
            PayScheduleConfirmed = ExistingIncomeMeta(id).Confirmed,
            StartsOn = EditorEmploymentStartDate is { } start
                ? DateOnly.FromDateTime(start)
                : ExistingIncomeMeta(id).StartsOn,
            EndsOn = ExistingIncomeMeta(id).EndsOn,
            Role = ExistingIncomeMeta(id).Role,
            Notes = ExistingIncomeMeta(id).Notes,
            AmountPerPeriod = VariableHours.Fixed(amount)
        };
    }

    private MileageReimbursement BuildMileage(Guid id, DateOnly anchor)
    {
        if (!AmountParsing.TryParseDecimal(EditorMiles, out var miles) || miles < 0m)
        {
            throw new ArgumentException("Enter miles reimbursed each pay period.");
        }

        if (!AmountParsing.TryParseMoney(EditorRatePerMile, out var rate) || rate.IsNegative)
        {
            throw new ArgumentException("Enter a valid rate per mile.");
        }

        return new MileageReimbursement
        {
            Id = id,
            Name = EditorName.Trim(),
            MemberId = EditorOwnerId,
            PayFrequency = EditorFrequency,
            AnchorPayDate = anchor,
            IsTaxable = false,
            IsActive = EditorIsActive,
            PayScheduleConfirmed = ExistingIncomeMeta(id).Confirmed,
            StartsOn = EditorEmploymentStartDate is { } start
                ? DateOnly.FromDateTime(start)
                : ExistingIncomeMeta(id).StartsOn,
            EndsOn = ExistingIncomeMeta(id).EndsOn,
            Role = IncomeRole.Reimbursement,
            Notes = ExistingIncomeMeta(id).Notes,
            MilesPerPeriod = miles,
            RatePerMile = rate
        };
    }

    private (bool Confirmed, string? Notes, DateOnly? StartsOn, DateOnly? EndsOn, IncomeRole Role) ExistingIncomeMeta(Guid id)
    {
        var existing = _session.Document.IncomeSources.FirstOrDefault(source => source.Id == id);
        return existing is null
            ? (true, null, null, null, IncomeRole.Wages)
            : (existing.PayScheduleConfirmed, existing.Notes, existing.StartsOn, existing.EndsOn, existing.Role);
    }

    private void OpenEditor(Guid? id, string title, IncomeSource? source)
    {
        ErrorMessage = null;
        _editingId = id;
        EditorTitle = title;

        if (source is null)
        {
            EditorName = string.Empty;
            EditorOwnerId = OwnerOptions.FirstOrDefault()?.Value ?? Guid.Empty;
            EditorKind = IncomeKindSelection.Hourly;
            EditorFrequency = Frequency.Weekly;
            EditorHourlyRate = string.Empty;
            EditorHoursConservative = "35";
            EditorHoursNormal = "37.5";
            EditorHoursOptimistic = "40";
            EditorOvertimeMultiplier = "1.5";
            EditorOvertimeConservative = "0";
            EditorOvertimeNormal = "0";
            EditorOvertimeOptimistic = "0";
            EditorAnnualSalary = string.Empty;
            EditorPeriodAmount = string.Empty;
            EditorMiles = string.Empty;
            EditorRatePerMile = string.Empty;
            EditorIsTaxable = true;
            EditorIsReimbursement = false;
            EditorStartDate = DateTime.Today;
            EditorEmploymentStartDate = null;
            EditorIsActive = true;
        }
        else
        {
            LoadSource(source);
        }

        _originalFingerprint = Fingerprint();
        IsEditorOpen = true;
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasEditorChanges));
        OnPropertyChanged(nameof(EquivalentSummary));
    }

    private void LoadSource(IncomeSource source)
    {
        EditorName = source.Name;
        EditorOwnerId = source.MemberId;
        EditorFrequency = source.PayFrequency;
        EditorIsTaxable = source.IsTaxable;
        EditorIsReimbursement = !source.IsTaxable;
        EditorStartDate = source.AnchorPayDate.ToDateTime(TimeOnly.MinValue);
        EditorEmploymentStartDate = source.StartsOn?.ToDateTime(TimeOnly.MinValue);
        EditorIsActive = source.IsActive;

        switch (source)
        {
            case HourlyIncome hourly:
                EditorKind = IncomeKindSelection.Hourly;
                EditorHourlyRate = AmountParsing.Format(hourly.HourlyRate);
                EditorHoursConservative = AmountParsing.Format(hourly.WeeklyHours.Conservative);
                EditorHoursNormal = AmountParsing.Format(hourly.WeeklyHours.Normal);
                EditorHoursOptimistic = AmountParsing.Format(hourly.WeeklyHours.Optimistic);
                EditorOvertimeMultiplier = AmountParsing.Format(hourly.OvertimeMultiplier);
                EditorOvertimeConservative = AmountParsing.Format(hourly.WeeklyOvertimeHours.Conservative);
                EditorOvertimeNormal = AmountParsing.Format(hourly.WeeklyOvertimeHours.Normal);
                EditorOvertimeOptimistic = AmountParsing.Format(hourly.WeeklyOvertimeHours.Optimistic);
                break;

            case SalaryIncome salary:
                EditorKind = IncomeKindSelection.Salary;
                EditorAnnualSalary = AmountParsing.Format(salary.AnnualSalary);
                break;

            case MileageReimbursement mileage:
                EditorKind = IncomeKindSelection.Mileage;
                EditorMiles = AmountParsing.Format(mileage.MilesPerPeriod);
                EditorRatePerMile = AmountParsing.Format(mileage.RatePerMile);
                EditorIsTaxable = false;
                EditorIsReimbursement = true;
                break;

            case VariableIncome variable:
                EditorKind = IncomeKindSelection.FixedRecurring;
                EditorPeriodAmount = AmountParsing.Format(variable.AmountPerPeriod.Normal);
                break;
        }
    }

    private string Fingerprint() => string.Join("|",
        EditorName,
        EditorOwnerId,
        EditorKind,
        EditorFrequency,
        EditorHourlyRate,
        EditorHoursConservative,
        EditorHoursNormal,
        EditorHoursOptimistic,
        EditorOvertimeMultiplier,
        EditorOvertimeConservative,
        EditorOvertimeNormal,
        EditorOvertimeOptimistic,
        EditorAnnualSalary,
        EditorPeriodAmount,
        EditorMiles,
        EditorRatePerMile,
        EditorIsTaxable,
        EditorIsReimbursement,
        EditorStartDate,
        EditorEmploymentStartDate,
        EditorIsActive);

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

        if (!_dialog.Confirm("Unsaved changes", "Discard the income entry you are editing?"))
        {
            return false;
        }

        CloseEditor();
        return true;
    }

    private void CloseEditor()
    {
        IsEditorOpen = false;
        _editingId = null;
        EditorName = string.Empty;
        EditorHourlyRate = string.Empty;
        EditorAnnualSalary = string.Empty;
        EditorPeriodAmount = string.Empty;
        EditorMiles = string.Empty;
        EditorRatePerMile = string.Empty;
        ErrorMessage = null;
        _originalFingerprint = string.Empty;
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasEditorChanges));
    }

    private async Task<bool> CommitAsync(BudgetDocument document, CancellationToken cancellationToken)
    {
        var result = await EditorSaveCoordinator.TryCommitAsync(
            _session,
            document,
            cancellationToken,
            "Income saved");
        ErrorMessage = result.IsSuccess ? null : result.Message;
        return result.IsSuccess;
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Items = [];
            ItemCount = 0;
            OwnerOptions = [];
            CloseEditor();
            StatusMessage = null;
            ErrorMessage = null;
            OnPropertyChanged(nameof(Items));
            OnPropertyChanged(nameof(OwnerOptions));
            OnPropertyChanged(nameof(CanAdd));
            OnPropertyChanged(nameof(CannotAddReason));
            BeginAddCommand.NotifyCanExecuteChanged();
            return;
        }

        var document = _session.Document;
        OwnerOptions = document.Members
            .Select(member => new ChoiceOption<Guid>(member.Id, member.Name))
            .ToList();

        Items = document.IncomeSources.Select(source =>
        {
            var annual = source.GrossPerYear(IncomeEstimate.Normal).Round();
            return new IncomeListItem(
                source.Id,
                source.Name,
                document.MemberName(source.MemberId),
                KindName(source),
                source.PayFrequency.ToDisplayName(),
                BudgetOverviewCalculator.ToPeriod(annual, DisplayPeriod.Weekly).ToDisplayString(),
                BudgetOverviewCalculator.ToPeriod(annual, DisplayPeriod.AverageMonthly).ToDisplayString(),
                annual.ToDisplayString(),
                source.IsActive ? "Active" : "Inactive",
                source is MileageReimbursement);
        }).ToList();

        ItemCount = Items.Count;
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(OwnerOptions));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CannotAddReason));
        BeginAddCommand.NotifyCanExecuteChanged();
        SaveEntryCommand.NotifyCanExecuteChanged();
    }

    private static string KindName(IncomeSource source) => source switch
    {
        HourlyIncome => "Hourly",
        SalaryIncome => "Salary",
        MileageReimbursement => "Mileage reimbursement",
        VariableIncome => source.IsTaxable ? "Fixed recurring" : "Reimbursement",
        _ => "Income"
    };
}
