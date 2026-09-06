using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Benefits;
using SecureBudgetManager.Core.Budgeting;
using SecureBudgetManager.Core.Models;
using SecureBudgetManager.Core.Storage;
using SecureBudgetManager.Core.Tax;
using SecureBudgetManager.Core.Time;

namespace SecureBudgetManager.App.ViewModels;

public sealed record DeductionListItem(
    int Index,
    string Name,
    string Amount,
    string Treatment,
    string Kind);

public sealed record BenefitListItem(
    Guid Id,
    string Name,
    string Kind,
    string Premium,
    string Frequency,
    string Weekly,
    string Annual,
    string Coverage,
    string Dates,
    string Status);

public sealed partial class PayrollViewModel : PageViewModel
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private Guid? _editingBenefitId;
    private string _originalBenefitFingerprint = string.Empty;
    private bool _suppressMemberLoad;

    public PayrollViewModel(IBudgetSession session, IUserDialog dialog)
        : base(
            "Payroll",
            "Deductions and benefits",
            "401(k) contributions, other payroll deductions and insurance premiums are stored separately. Employer match is shown but never counted as take-home pay.")
    {
        _session = session;
        _dialog = dialog;
        _session.Changed += OnSessionChanged;
        TreatmentOptions =
        [
            new ChoiceOption<DeductionTaxTreatment>(DeductionTaxTreatment.PreTaxIncludingFica, "Pre-tax (including FICA)"),
            new ChoiceOption<DeductionTaxTreatment>(DeductionTaxTreatment.PreTaxIncomeTaxOnly, "Pre-tax (income tax only)"),
            new ChoiceOption<DeductionTaxTreatment>(DeductionTaxTreatment.PostTax, "Post-tax")
        ];
        BenefitKindOptions =
        [
            new ChoiceOption<BenefitKind>(BenefitKind.Medical, "Medical"),
            new ChoiceOption<BenefitKind>(BenefitKind.Dental, "Dental"),
            new ChoiceOption<BenefitKind>(BenefitKind.Vision, "Vision"),
            new ChoiceOption<BenefitKind>(BenefitKind.Life, "Life"),
            new ChoiceOption<BenefitKind>(BenefitKind.Accident, "Accident"),
            new ChoiceOption<BenefitKind>(BenefitKind.CriticalIllness, "Critical illness"),
            new ChoiceOption<BenefitKind>(BenefitKind.HospitalIndemnity, "Hospital indemnity"),
            new ChoiceOption<BenefitKind>(BenefitKind.AccidentalDeathAndDismemberment, "AD&D"),
            new ChoiceOption<BenefitKind>(BenefitKind.LegalPlan, "Legal plan"),
            new ChoiceOption<BenefitKind>(BenefitKind.Other, "Voluntary / other")
        ];
        FrequencyOptions = FrequencyChoices.Expense;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<DeductionTaxTreatment>> TreatmentOptions { get; }

    public IReadOnlyList<ChoiceOption<BenefitKind>> BenefitKindOptions { get; }

    public IReadOnlyList<ChoiceOption<Frequency>> FrequencyOptions { get; }

    public IReadOnlyList<ChoiceOption<FilingStatus>> FilingStatusOptions { get; } =
    [
        new(FilingStatus.Single, "Single"),
        new(FilingStatus.MarriedFilingJointly, "Married filing jointly"),
        new(FilingStatus.MarriedFilingSeparately, "Married filing separately"),
        new(FilingStatus.HeadOfHousehold, "Head of household")
    ];

    public IReadOnlyList<ChoiceOption<int>> TaxYearOptions { get; } =
        TaxYearLibrary.BuiltIn.Select(table => new ChoiceOption<int>(table.Year, $"{table.Year} (estimate)")).ToList();

    public IReadOnlyList<ChoiceOption<Guid>> MemberOptions { get; private set; } = [];

    public IReadOnlyList<DeductionListItem> Deductions { get; private set; } = [];

    public IReadOnlyList<BenefitListItem> Benefits { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMember))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private Guid selectedMemberId;

    public bool HasMember => SelectedMemberId != Guid.Empty;

    public bool ShowEmptyState => _session.IsOpen && MemberOptions.Count == 0;

    [ObservableProperty]
    private string payFrequencyLabel = "No income schedule yet. Amounts are stored per pay period.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRetirementCommand))]
    private string employeeContributionPercent = "0";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRetirementCommand))]
    private string employerMatchPercent = "0";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRetirementCommand))]
    private string employerMatchLimitPercent = "0";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRetirementCommand))]
    private bool isRoth;

    [ObservableProperty]
    private string employerMatchSummary = "Employer match is not take-home pay.";

    [ObservableProperty]
    private int taxYear = 2025;

    [ObservableProperty]
    private FilingStatus filingStatus = FilingStatus.Single;

    [ObservableProperty]
    private string qualifyingChildren = "0";

    [ObservableProperty]
    private string otherDependants = "0";

    [ObservableProperty]
    private string extraWithholding = "0";

    [ObservableProperty]
    private string otherAnnualIncome = "0";

    [ObservableProperty]
    private string annualDeductions = "0";

    [ObservableProperty]
    private string stateRatePercent = "0";

    [ObservableProperty]
    private bool multipleJobsChecked;

    [ObservableProperty]
    private bool isNonResidentAlien;

    [ObservableProperty]
    private string estimatedNetPayText = "Save withholding to estimate net pay. Estimates are not tax advice.";

    [ObservableProperty]
    private string taxTableNote = TaxYearLibrary.NotTaxAdviceWarning;

    [ObservableProperty]
    private string deductionName = string.Empty;

    [ObservableProperty]
    private string deductionAmount = string.Empty;

    [ObservableProperty]
    private DeductionTaxTreatment deductionTreatment = DeductionTaxTreatment.PreTaxIncludingFica;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBenefitEditor))]
    private bool isBenefitEditorOpen;

    public bool ShowBenefitEditor => IsBenefitEditorOpen;

    [ObservableProperty]
    private string benefitEditorTitle = "Add benefit";

    [ObservableProperty]
    private string benefitName = string.Empty;

    [ObservableProperty]
    private BenefitKind benefitKind = BenefitKind.Medical;

    [ObservableProperty]
    private string benefitPremium = string.Empty;

    [ObservableProperty]
    private Frequency benefitFrequency = Frequency.Weekly;

    [ObservableProperty]
    private DeductionTaxTreatment benefitTreatment = DeductionTaxTreatment.PreTaxIncludingFica;

    [ObservableProperty]
    private string benefitEmployerContribution = "0";

    [ObservableProperty]
    private DateTime? benefitEffectiveDate = DateTime.Today;

    [ObservableProperty]
    private DateTime? benefitRenewalDate;

    [ObservableProperty]
    private string benefitCoverageAmount = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnSelectedMemberIdChanged(Guid value)
    {
        if (_suppressMemberLoad)
        {
            return;
        }

        LoadMember(value);
    }

    [RelayCommand(CanExecute = nameof(CanSaveRetirement))]
    private async Task SaveRetirementAsync(CancellationToken cancellationToken)
    {
        if (!HasMember)
        {
            ErrorMessage = "Choose a household member first.";
            return;
        }

        if (!AmountParsing.TryParseDecimal(EmployeeContributionPercent, out var employee)
            || !AmountParsing.TryParseDecimal(EmployerMatchPercent, out var match)
            || !AmountParsing.TryParseDecimal(EmployerMatchLimitPercent, out var limit))
        {
            ErrorMessage = "Enter valid 401(k) percentages.";
            return;
        }

        var profile = CurrentProfile() with
        {
            Retirement = new RetirementPlan
            {
                EmployeeContributionPercent = employee,
                EmployerMatchPercent = match,
                EmployerMatchLimitPercent = limit,
                IsRoth = IsRoth,
                VestingYears = CurrentProfile().Retirement.VestingYears
            }
        };

        if (!await ReplaceProfileAsync(profile, cancellationToken))
        {
            return;
        }

        StatusMessage = "401(k) settings saved. Employer match is listed separately and is not take-home pay.";
    }

    private bool CanSaveRetirement() => _session.IsOpen && !_session.IsSaving && HasMember;

    [RelayCommand(CanExecute = nameof(CanSaveRetirement))]
    private async Task SaveWithholdingAsync(CancellationToken cancellationToken)
    {
        if (!HasMember)
        {
            ErrorMessage = "Choose a household member first.";
            return;
        }

        if (!int.TryParse(QualifyingChildren, out var children) || children < 0
            || !int.TryParse(OtherDependants, out var dependants) || dependants < 0)
        {
            ErrorMessage = "Dependant counts must be whole numbers.";
            return;
        }

        if (!AmountParsing.TryParseMoney(ExtraWithholding, out var extra)
            || !AmountParsing.TryParseMoney(OtherAnnualIncome, out var otherIncome)
            || !AmountParsing.TryParseMoney(AnnualDeductions, out var deductions)
            || !AmountParsing.TryParseDecimal(StateRatePercent, out var statePercent)
            || extra.IsNegative || otherIncome.IsNegative || deductions.IsNegative
            || statePercent < 0m)
        {
            ErrorMessage = "Enter valid withholding amounts and a state rate of 0 or more.";
            return;
        }

        var profile = CurrentProfile() with
        {
            TaxYear = TaxYear,
            StateFlatRate = statePercent / 100m,
            UsesUtahWithholding = UsesUtahWithholding,
            TaxResidency = TaxResidency,
            W4 = new W4Settings
            {
                FilingStatus = FilingStatus,
                QualifyingChildren = children,
                OtherDependants = dependants,
                ExtraWithholdingPerPeriod = extra,
                OtherAnnualIncome = otherIncome,
                AnnualDeductions = deductions,
                MultipleJobsChecked = MultipleJobsChecked,
                IsNonResidentAlien = IsNonResidentAlien
            }
        };

        if (!await ReplaceProfileAsync(profile, cancellationToken))
        {
            return;
        }

        StatusMessage = "Withholding saved. Dashboard tax figures are estimates for the selected tax year, not tax advice.";
    }

    [ObservableProperty]
    private bool usesUtahWithholding;

    [ObservableProperty]
    private UsTaxResidency taxResidency = UsTaxResidency.NotYetDetermined;

    [ObservableProperty]
    private string utahWithholdingSummary = string.Empty;

    public IReadOnlyList<ChoiceOption<UsTaxResidency>> TaxResidencyOptions { get; } =
        Enum.GetValues<UsTaxResidency>()
            .Select(status => new ChoiceOption<UsTaxResidency>(status, status.ToDisplayName()))
            .ToList();

    [RelayCommand]
    private void ApplyUtahWithholding()
    {
        UsesUtahWithholding = true;
        StateRatePercent = "0";
        StatusMessage =
            "Utah Publication 14 withholding will be used. The 2025 statutory rate is 4.50% and the " +
            "2026 statutory rate is 4.45%. Withholding for pay periods beginning on or after 1 June 2026 " +
            "uses the revised 4.45% formula. This is an estimate, not tax advice.";
    }

    [RelayCommand]
    private async Task AddDeductionAsync(CancellationToken cancellationToken)
    {
        if (!HasMember)
        {
            ErrorMessage = "Choose a household member first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(DeductionName))
        {
            ErrorMessage = "A deduction needs a name.";
            return;
        }

        if (!AmountParsing.TryParseMoney(DeductionAmount, out var amount) || amount.IsNegative)
        {
            ErrorMessage = "Enter a valid amount per pay period.";
            return;
        }

        var profile = CurrentProfile();
        var deductions = profile.Deductions.ToList();
        deductions.Add(new PayrollDeduction
        {
            Name = DeductionName.Trim(),
            AmountPerPeriod = amount,
            TaxTreatment = DeductionTreatment
        });

        if (!await ReplaceProfileAsync(profile with { Deductions = deductions }, cancellationToken))
        {
            return;
        }

        DeductionName = string.Empty;
        DeductionAmount = string.Empty;
        StatusMessage = "Deduction saved.";
    }

    [RelayCommand]
    private async Task RemoveDeductionAsync(DeductionListItem? item, CancellationToken cancellationToken)
    {
        if (item is null || !HasMember)
        {
            return;
        }

        if (!_dialog.Confirm("Remove deduction", $"Remove the deduction named {item.Name}?"))
        {
            return;
        }

        var profile = CurrentProfile();
        var deductions = profile.Deductions.ToList();
        if (item.Index < 0 || item.Index >= deductions.Count)
        {
            return;
        }

        deductions.RemoveAt(item.Index);
        if (!await ReplaceProfileAsync(profile with { Deductions = deductions }, cancellationToken))
        {
            return;
        }

        StatusMessage = "Deduction removed.";
    }

    [RelayCommand]
    private void BeginAddBenefit()
    {
        if (!HasMember)
        {
            ErrorMessage = "Choose a household member first.";
            return;
        }

        if (!ConfirmDiscardBenefit())
        {
            return;
        }

        _editingBenefitId = null;
        BenefitEditorTitle = "Add benefit";
        BenefitName = string.Empty;
        BenefitKind = BenefitKind.Medical;
        BenefitPremium = string.Empty;
        BenefitFrequency = InferPayFrequency();
        BenefitTreatment = DeductionTaxTreatment.PreTaxIncludingFica;
        BenefitEmployerContribution = "0";
        BenefitCoverageAmount = "0";
        BenefitEffectiveDate = DateTime.Today;
        BenefitRenewalDate = null;
        _originalBenefitFingerprint = BenefitFingerprint();
        IsBenefitEditorOpen = true;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void BeginEditBenefit(BenefitListItem? item)
    {
        if (item is null || !ConfirmDiscardBenefit())
        {
            return;
        }

        var benefit = _session.Document.Benefits.FirstOrDefault(plan => plan.Id == item.Id);
        if (benefit is null)
        {
            return;
        }

        _editingBenefitId = benefit.Id;
        BenefitEditorTitle = "Edit benefit";
        BenefitName = benefit.Name;
        BenefitKind = benefit.Kind;
        BenefitPremium = AmountParsing.Format(benefit.EmployeePremiumPerPeriod);
        BenefitFrequency = benefit.PremiumFrequency;
        BenefitTreatment = benefit.TaxTreatment;
        BenefitEmployerContribution = AmountParsing.Format(benefit.EmployerContributionPerPeriod);
        BenefitCoverageAmount = AmountParsing.Format(benefit.CoverageAmount);
        BenefitEffectiveDate = benefit.EffectiveDate?.ToDateTime(TimeOnly.MinValue);
        BenefitRenewalDate = benefit.RenewalDate?.ToDateTime(TimeOnly.MinValue);
        _originalBenefitFingerprint = BenefitFingerprint();
        IsBenefitEditorOpen = true;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task SaveBenefitAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildBenefit(_editingBenefitId ?? Guid.NewGuid(), out var benefit, out var error))
        {
            ErrorMessage = error;
            return;
        }

        var document = _session.Document;
        var benefits = document.Benefits.ToList();

        if (_editingBenefitId is { } id)
        {
            var index = benefits.FindIndex(plan => plan.Id == id);
            if (index < 0)
            {
                ErrorMessage = "That benefit is no longer available.";
                return;
            }

            benefits[index] = benefit with { Id = id };
        }
        else
        {
            benefits.Add(benefit);
        }

        if (!EnsureProfileExists(document, out document))
        {
            return;
        }

        if (!await CommitAsync(document with { Benefits = benefits }, cancellationToken))
        {
            return;
        }

        CloseBenefitEditor();
        StatusMessage = "Benefit saved.";
    }

    [RelayCommand]
    private void CancelBenefit()
    {
        if (!ConfirmDiscardBenefit(forcePrompt: IsBenefitEditorOpen && BenefitFingerprint() != _originalBenefitFingerprint))
        {
            return;
        }

        CloseBenefitEditor();
    }

    [RelayCommand]
    private async Task RemoveBenefitAsync(BenefitListItem? item, CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return;
        }

        if (!_dialog.Confirm("Remove benefit", $"Remove the benefit named {item.Name}?"))
        {
            return;
        }

        var remaining = _session.Document.Benefits.Where(plan => plan.Id != item.Id).ToList();
        if (!await CommitAsync(_session.Document with { Benefits = remaining }, cancellationToken))
        {
            return;
        }

        if (_editingBenefitId == item.Id)
        {
            CloseBenefitEditor();
        }

        StatusMessage = "Benefit removed.";
    }

    public string? ValidateDeduction()
    {
        if (string.IsNullOrWhiteSpace(DeductionName))
        {
            return "A deduction needs a name.";
        }

        if (!AmountParsing.TryParseMoney(DeductionAmount, out var amount) || amount.IsNegative)
        {
            return "Enter a valid amount per pay period.";
        }

        return null;
    }

    public string? ValidateBenefit()
    {
        TryBuildBenefit(Guid.NewGuid(), out _, out var error);
        return error;
    }

    private bool TryBuildBenefit(Guid id, out BenefitPlan benefit, out string? error)
    {
        benefit = null!;
        error = null;

        if (!HasMember)
        {
            error = "Choose a household member first.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(BenefitName))
        {
            error = "A benefit needs a name.";
            return false;
        }

        if (!AmountParsing.TryParseMoney(BenefitPremium, out var premium) || premium.IsNegative)
        {
            error = "Enter a valid employee premium per period.";
            return false;
        }

        AmountParsing.TryParseMoney(BenefitEmployerContribution, out var employer);
        AmountParsing.TryParseMoney(BenefitCoverageAmount, out var coverage);

        try
        {
            var existing = _session.Document.Benefits.FirstOrDefault(item => item.Id == id);

            benefit = new BenefitPlan
            {
                Id = id,
                Name = BenefitName.Trim(),
                Kind = BenefitKind,
                MemberId = SelectedMemberId,
                EmployeePremiumPerPeriod = premium,
                PremiumFrequency = BenefitFrequency,
                EmployerContributionPerPeriod = employer,
                CoverageAmount = coverage,
                TaxTreatment = BenefitTreatment,
                EffectiveDate = BenefitEffectiveDate is null
                    ? null
                    : DateOnly.FromDateTime(BenefitEffectiveDate.Value),
                RenewalDate = BenefitRenewalDate is null
                    ? null
                    : DateOnly.FromDateTime(BenefitRenewalDate.Value),
                IsConfirmed = existing?.IsConfirmed ?? true,
                Notes = existing?.Notes,
                Coverage = existing?.Coverage ?? CoverageLevel.EmployeeOnly,
                Deductible = existing?.Deductible ?? Money.Zero,
                OutOfPocketMaximum = existing?.OutOfPocketMaximum ?? Money.Zero,
                Beneficiaries = existing?.Beneficiaries ?? []
            };

            benefit.Validate();
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private PayrollProfile CurrentProfile()
    {
        return _session.Document.PayrollProfiles.FirstOrDefault(profile => profile.MemberId == SelectedMemberId)
            ?? new PayrollProfile { MemberId = SelectedMemberId, TaxYear = 2025 };
    }

    private async Task<bool> ReplaceProfileAsync(PayrollProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            profile.Validate();
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = exception.Message;
            return false;
        }

        var document = _session.Document;
        var profiles = document.PayrollProfiles.Where(item => item.MemberId != profile.MemberId).ToList();
        profiles.Add(profile);
        return await CommitAsync(document with { PayrollProfiles = profiles }, cancellationToken);
    }

    private bool EnsureProfileExists(BudgetDocument document, out BudgetDocument next)
    {
        if (document.PayrollProfiles.Any(profile => profile.MemberId == SelectedMemberId))
        {
            next = document;
            return true;
        }

        var profiles = document.PayrollProfiles.ToList();
        profiles.Add(new PayrollProfile { MemberId = SelectedMemberId, TaxYear = 2025 });
        next = document with { PayrollProfiles = profiles };
        return true;
    }

    private Frequency InferPayFrequency()
    {
        var source = _session.Document.IncomeSources
            .FirstOrDefault(item => item.MemberId == SelectedMemberId && item.IsActive && item.IsTaxable)
            ?? _session.Document.IncomeSources.FirstOrDefault(item => item.MemberId == SelectedMemberId);

        return source?.PayFrequency ?? Frequency.Weekly;
    }

    private string BenefitFingerprint() => string.Join("|",
        BenefitName,
        BenefitKind,
        BenefitPremium,
        BenefitFrequency,
        BenefitTreatment,
        BenefitEmployerContribution,
        BenefitCoverageAmount,
        BenefitEffectiveDate,
        BenefitRenewalDate);

    private bool ConfirmDiscardBenefit(bool forcePrompt = false)
    {
        if (!IsBenefitEditorOpen)
        {
            return true;
        }

        var dirty = BenefitFingerprint() != _originalBenefitFingerprint;
        if (!dirty && !forcePrompt)
        {
            CloseBenefitEditor();
            return true;
        }

        if (!_dialog.Confirm("Unsaved changes", "Discard the benefit you are editing?"))
        {
            return false;
        }

        CloseBenefitEditor();
        return true;
    }

    private void CloseBenefitEditor()
    {
        IsBenefitEditorOpen = false;
        _editingBenefitId = null;
        BenefitName = string.Empty;
        BenefitPremium = string.Empty;
        BenefitCoverageAmount = string.Empty;
        ErrorMessage = null;
        _originalBenefitFingerprint = string.Empty;
    }

    private async Task<bool> CommitAsync(BudgetDocument document, CancellationToken cancellationToken)
    {
        ErrorMessage = null;

        if (!_session.TryReplace(document, out var error))
        {
            ErrorMessage = error;
            return false;
        }

        if (await _session.SaveAsync(cancellationToken))
        {
            return true;
        }

        ErrorMessage = _session.LastError ?? "The household data could not be saved.";
        return false;
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            _suppressMemberLoad = true;
            MemberOptions = [];
            SelectedMemberId = Guid.Empty;
            Deductions = [];
            Benefits = [];
            CloseBenefitEditor();
            EmployeeContributionPercent = "0";
            EmployerMatchPercent = "0";
            EmployerMatchLimitPercent = "0";
            IsRoth = false;
            DeductionName = string.Empty;
            DeductionAmount = string.Empty;
            StatusMessage = null;
            ErrorMessage = null;
            PayFrequencyLabel = string.Empty;
            EmployerMatchSummary = string.Empty;
            TaxYear = 2025;
            FilingStatus = FilingStatus.Single;
            QualifyingChildren = "0";
            OtherDependants = "0";
            BenefitCoverageAmount = string.Empty;
            ExtraWithholding = string.Empty;
            OtherAnnualIncome = string.Empty;
            AnnualDeductions = string.Empty;
            StateRatePercent = string.Empty;
            UsesUtahWithholding = false;
            TaxResidency = UsTaxResidency.NotYetDetermined;
            UtahWithholdingSummary = string.Empty;
            MultipleJobsChecked = false;
            IsNonResidentAlien = false;
            EstimatedNetPayText = string.Empty;
            _suppressMemberLoad = false;
            OnPropertyChanged(nameof(MemberOptions));
            OnPropertyChanged(nameof(Deductions));
            OnPropertyChanged(nameof(Benefits));
            OnPropertyChanged(nameof(ShowEmptyState));
            return;
        }

        var document = _session.Document;
        MemberOptions = document.Members
            .Where(member => !member.IsDependant)
            .Select(member => new ChoiceOption<Guid>(member.Id, member.Name))
            .ToList();

        _suppressMemberLoad = true;
        if (MemberOptions.All(option => option.Value != SelectedMemberId))
        {
            SelectedMemberId = MemberOptions.FirstOrDefault()?.Value ?? Guid.Empty;
        }

        _suppressMemberLoad = false;
        LoadMember(SelectedMemberId);
        OnPropertyChanged(nameof(MemberOptions));
        OnPropertyChanged(nameof(ShowEmptyState));
        SaveRetirementCommand.NotifyCanExecuteChanged();
        SaveWithholdingCommand.NotifyCanExecuteChanged();
    }

    private void LoadMember(Guid memberId)
    {
        if (memberId == Guid.Empty)
        {
            Deductions = [];
            Benefits = [];
            OnPropertyChanged(nameof(Deductions));
            OnPropertyChanged(nameof(Benefits));
            return;
        }

        var document = _session.Document;
        var profile = document.PayrollProfiles.FirstOrDefault(item => item.MemberId == memberId);
        var retirement = profile?.Retirement ?? new RetirementPlan();

        EmployeeContributionPercent = AmountParsing.Format(retirement.EmployeeContributionPercent);
        EmployerMatchPercent = AmountParsing.Format(retirement.EmployerMatchPercent);
        EmployerMatchLimitPercent = AmountParsing.Format(retirement.EmployerMatchLimitPercent);
        IsRoth = retirement.IsRoth;
        TaxYear = profile?.TaxYear ?? 2025;
        var w4 = profile?.W4 ?? new W4Settings();
        FilingStatus = w4.FilingStatus;
        QualifyingChildren = w4.QualifyingChildren.ToString();
        OtherDependants = w4.OtherDependants.ToString();
        ExtraWithholding = AmountParsing.Format(w4.ExtraWithholdingPerPeriod);
        OtherAnnualIncome = AmountParsing.Format(w4.OtherAnnualIncome);
        AnnualDeductions = AmountParsing.Format(w4.AnnualDeductions);
        StateRatePercent = AmountParsing.Format((profile?.StateFlatRate ?? 0m) * 100m);
        UsesUtahWithholding = profile?.UsesUtahWithholding ?? false;
        TaxResidency = profile?.TaxResidency ?? UsTaxResidency.NotYetDetermined;
        UtahWithholdingSummary = UsesUtahWithholding
            ? "Utah Publication 14 formula. Estimated withholding. Not tax advice."
            : string.Empty;
        MultipleJobsChecked = w4.MultipleJobsChecked;
        IsNonResidentAlien = w4.IsNonResidentAlien;

        var frequency = InferPayFrequency();
        PayFrequencyLabel =
            $"Amounts below are per {frequency.ToDisplayName().ToLowerInvariant()} pay period, taken from this member's income schedule.";

        var takeHome = TakeHomeCalculator.From(document, Core.Income.IncomeEstimate.Normal);
        EmployerMatchSummary = takeHome.AnnualEmployerMatch.IsZero
            ? "Employer match is not take-home pay. None is configured yet."
            : $"Employer match (not take-home): {BudgetOverviewCalculator.ToPeriod(takeHome.AnnualEmployerMatch, DisplayPeriod.AverageMonthly).ToDisplayString()} average monthly.";
        EstimatedNetPayText = profile is null
            ? "Save withholding so estimated taxes and net pay can be calculated for this tax year."
            : $"Estimated net pay {BudgetOverviewCalculator.ToPeriod(takeHome.AnnualTakeHome, DisplayPeriod.AverageMonthly).ToDisplayString()} average monthly. {TaxYearLibrary.NotTaxAdviceWarning}";

        var items = new List<DeductionListItem>();
        if (profile is not null)
        {
            for (var i = 0; i < profile.Deductions.Count; i++)
            {
                var deduction = profile.Deductions[i];
                items.Add(new DeductionListItem(
                    i,
                    deduction.Name,
                    deduction.AmountPerPeriod.ToDisplayString() + " / period",
                    TreatmentName(deduction.TaxTreatment),
                    deduction.TaxTreatment == DeductionTaxTreatment.PostTax ? "Post-tax" : "Pre-tax"));
            }
        }

        Deductions = items;
        Benefits = document.Benefits
            .Where(plan => plan.MemberId == memberId)
            .Select(plan => new BenefitListItem(
                plan.Id,
                plan.Name,
                plan.Kind.ToString(),
                plan.EmployeePremiumPerPeriod.ToDisplayString(),
                plan.PremiumFrequency.ToDisplayName(),
                FrequencyConverter.Convert(plan.EmployeePremiumPerPeriod, plan.PremiumFrequency, Frequency.Weekly).ToDisplayString() + " / week",
                plan.AnnualEmployeePremium.ToDisplayString() + " / year",
                plan.CoverageAmount.IsZero ? "No coverage amount" : plan.CoverageAmount.ToDisplayString(),
                DateLabel(plan),
                StatusLabel(plan)))
            .ToList();

        OnPropertyChanged(nameof(Deductions));
        OnPropertyChanged(nameof(Benefits));
    }

    private static string TreatmentName(DeductionTaxTreatment treatment) => treatment switch
    {
        DeductionTaxTreatment.PreTaxIncludingFica => "Pre-tax (including FICA)",
        DeductionTaxTreatment.PreTaxIncomeTaxOnly => "Pre-tax (income tax only)",
        DeductionTaxTreatment.PostTax => "Post-tax",
        _ => treatment.ToString()
    };

    private static string DateLabel(BenefitPlan plan)
    {
        if (plan.EffectiveDate is null && plan.RenewalDate is null)
        {
            return "No dates recorded";
        }

        var start = plan.EffectiveDate is { } effective ? effective.ToString("yyyy-MM-dd") : "—";
        var end = plan.RenewalDate is { } renewal ? renewal.ToString("yyyy-MM-dd") : "—";
        return $"{start} to {end}";
    }

    private static string StatusLabel(BenefitPlan plan)
    {
        if (plan.RenewalDate is { } renewal && renewal < DateOnly.FromDateTime(DateTime.Today))
        {
            return "Ended";
        }

        return "Active";
    }
}
