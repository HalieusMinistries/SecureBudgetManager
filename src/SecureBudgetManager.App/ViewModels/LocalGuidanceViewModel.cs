using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureBudgetManager.App.Interaction;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Guidance;

namespace SecureBudgetManager.App.ViewModels;

public sealed record GuidanceRow(
    Guid Id,
    string Category,
    string Locality,
    string Composition,
    string Amounts,
    string Source,
    string SourceType,
    string Observed,
    string Effective,
    string Confidence,
    string LastReviewed,
    string ReviewBy,
    string ReviewState,
    string PackVersion,
    string UserSelected,
    string Difference,
    string Notes,
    bool IsSelected = false);

/// <summary>
/// Local cost references. A household new to Utah has no feel for what a week's shopping costs
/// here, and the honest answer when nothing is recorded is to say so rather than invent a figure.
/// </summary>
public sealed partial class LocalGuidanceViewModel : PageViewModel, IEditablePage
{
    private readonly IBudgetSession _session;
    private readonly IUserDialog _dialog;
    private readonly TimeProvider _clock;

    public LocalGuidanceViewModel(IBudgetSession session, IUserDialog dialog, TimeProvider clock)
        : base(
            "Local guidance",
            "Utah costs",
            "What things cost here, with the source and date behind every figure. Nothing is " +
            "downloaded and nothing is sent anywhere: every record is one you or a household " +
            "member entered, and outdated records are labelled rather than quietly reused.")
    {
        _session = session;
        _dialog = dialog;
        _clock = clock;
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public IReadOnlyList<ChoiceOption<CostGuidanceSourceType>> SourceTypes { get; } =
        Enum.GetValues<CostGuidanceSourceType>()
            .Select(type => new ChoiceOption<CostGuidanceSourceType>(type, type.ToDisplayName()))
            .ToList();

    public IReadOnlyList<ChoiceOption<GuidanceConfidence>> Confidences { get; } =
    [
        new(GuidanceConfidence.Low, "Low"),
        new(GuidanceConfidence.Medium, "Medium"),
        new(GuidanceConfidence.High, "High")
    ];

    public IReadOnlyList<string> CategoryChoices { get; } =
        GroceryPlanner.DefaultCategories
            .Select(entry => entry.Name)
            .Concat(GuidanceCategories.KnownCategories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

    [ObservableProperty]
    private string householdState = string.Empty;

    [ObservableProperty]
    private string householdCounty = string.Empty;

    [ObservableProperty]
    private string householdCity = string.Empty;

    [ObservableProperty]
    private string localitySummary = string.Empty;

    [ObservableProperty]
    private string reviewSummary = string.Empty;

    [ObservableProperty]
    private GuidanceRow? selectedRecord;

    [ObservableProperty]
    private string category = string.Empty;

    [ObservableProperty]
    private string recordState = string.Empty;

    [ObservableProperty]
    private string recordCounty = string.Empty;

    [ObservableProperty]
    private string recordCity = string.Empty;

    [ObservableProperty]
    private string adults = "2";

    [ObservableProperty]
    private string children = "0";

    [ObservableProperty]
    private string low = string.Empty;

    [ObservableProperty]
    private string typical = string.Empty;

    [ObservableProperty]
    private string comfortable = string.Empty;

    [ObservableProperty]
    private string sourceName = string.Empty;

    [ObservableProperty]
    private CostGuidanceSourceType sourceType = CostGuidanceSourceType.LocallyObservedPrice;

    [ObservableProperty]
    private GuidanceConfidence confidence = GuidanceConfidence.Medium;

    [ObservableProperty]
    private DateTime? observedOn;

    [ObservableProperty]
    private DateTime? effectiveDate;

    [ObservableProperty]
    private DateTime? reviewByDate;

    [ObservableProperty]
    private string notes = string.Empty;

    [ObservableProperty]
    private string userSelected = string.Empty;

    [ObservableProperty]
    private string packSummary = string.Empty;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    private bool isEditorOpen;

    [ObservableProperty]
    private double listScrollOffset;

    [ObservableProperty]
    private Guid? selectedRecordId;

    private Guid? _editingId;
    private string _originalFingerprint = string.Empty;

    public IReadOnlyList<GuidanceRow> Records { get; private set; } = [];

    public IReadOnlyList<string> ReviewWarnings { get; private set; } = [];

    public bool HasRecords => Records.Count > 0;

    public string EditorTitle => _editingId is null ? "Add guidance figure" : "Edit guidance figure";

    public string EditorSaveLabel => "Save guidance";

    public string? EditorEffectPreview =>
        "A figure with no source is not guidance. Outdated records stay visible and are labelled for review.";

    public bool HasEditorChanges => IsEditorOpen && GuidanceFingerprint() != _originalFingerprint;

    public bool HasEditorError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? EditorError => ErrorMessage;

    public ICommand SaveEditorCommand => SaveRecordCommand;

    public ICommand CancelEditorCommand => CancelGuidanceEditorCommand;

    public bool TryLeaveEditor()
    {
        if (!IsEditorOpen)
        {
            return true;
        }

        if (!HasEditorChanges || _dialog.Confirm("Unsaved changes", "Close without saving this guidance figure?"))
        {
            DismissEditor();
            return true;
        }

        return false;
    }

    public void DismissEditor()
    {
        IsEditorOpen = false;
        _editingId = null;
        ErrorMessage = null;
    }

    private string GuidanceFingerprint() =>
        $"{Category}|{RecordState}|{RecordCounty}|{RecordCity}|{Adults}|{Children}|{Low}|{Typical}|{Comfortable}|{SourceName}|{SourceType}|{Confidence}|{ObservedOn}|{EffectiveDate}|{ReviewByDate}|{Notes}|{UserSelected}";

    [RelayCommand]
    private void CancelGuidanceEditor() => DismissEditor();

    [RelayCommand]
    private void SelectRecord(GuidanceRow? row)
    {
        if (row is null)
        {
            return;
        }

        SelectedRecordId = row.Id;
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
        SelectedRecord = null;
        Category = string.Empty;
        RecordState = HouseholdState;
        RecordCounty = HouseholdCounty;
        RecordCity = HouseholdCity;
        Adults = _session.IsOpen
            ? _session.Document.Composition.Adults.ToString()
            : "2";
        Children = _session.IsOpen
            ? _session.Document.Composition.Children.ToString()
            : "0";
        Low = string.Empty;
        Typical = string.Empty;
        Comfortable = string.Empty;
        SourceName = string.Empty;
        SourceType = CostGuidanceSourceType.LocallyObservedPrice;
        Confidence = GuidanceConfidence.Medium;
        ObservedOn = null;
        EffectiveDate = null;
        ReviewByDate = null;
        Notes = string.Empty;
        UserSelected = string.Empty;
        _originalFingerprint = GuidanceFingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void BeginEdit(GuidanceRow? row)
    {
        if (row is null || !_session.IsOpen || !TryLeaveEditor())
        {
            return;
        }

        var record = _session.Document.CostGuidance.FirstOrDefault(item => item.Id == row.Id);
        if (record is null)
        {
            return;
        }

        SelectedRecordId = record.Id;
        _editingId = record.Id;
        ApplyRecord(record);
        _originalFingerprint = GuidanceFingerprint();
        ErrorMessage = null;
        IsEditorOpen = true;
        OnPropertyChanged(nameof(EditorTitle));
        RematchSelection();
    }

    [RelayCommand]
    private async Task SaveLocalityAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        var locality = new CostLocality
        {
            Country = "United States",
            State = Blank(HouseholdState),
            County = Blank(HouseholdCounty),
            City = Blank(HouseholdCity)
        };

        if (!_session.TryReplace(_session.Document with { Locality = locality }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? $"Guidance will now be matched to {locality.Describe()}."
            : _session.LastError ?? "The locality could not be saved.";
    }

    [RelayCommand]
    private async Task SaveRecordAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Category))
        {
            ErrorMessage = "Choose or enter the category this figure is for.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SourceName))
        {
            ErrorMessage = "Record where the figure came from. A figure with no source is not guidance.";
            return;
        }

        if (!AmountParsing.TryParseMoney(Blank(Low) ?? "0", out var low)
            || !AmountParsing.TryParseMoney(Blank(Typical) ?? "0", out var typical)
            || !AmountParsing.TryParseMoney(Blank(Comfortable) ?? "0", out var comfortable))
        {
            ErrorMessage = "Enter the low, typical and comfortable amounts as numbers.";
            return;
        }

        if (!int.TryParse(Adults, out var adults) || adults < 0
            || !int.TryParse(Children, out var children) || children < 0)
        {
            ErrorMessage = "Enter how many adults and children the figure is priced for.";
            return;
        }

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

        var record = new CostGuidanceRecord
        {
            Id = _editingId ?? SelectedRecord?.Id ?? Guid.NewGuid(),
            Locality = new CostLocality
            {
                Country = "United States",
                State = Blank(RecordState),
                County = Blank(RecordCounty),
                City = Blank(RecordCity)
            },
            Composition = new HouseholdComposition(adults, children),
            Category = Category.Trim(),
            EffectiveDate = EffectiveDate is { } effective ? DateOnly.FromDateTime(effective) : today,
            SourceName = SourceName.Trim(),
            SourceType = SourceType,
            ObservedOn = ObservedOn is { } observed ? DateOnly.FromDateTime(observed) : null,
            Low = low,
            Typical = typical,
            Comfortable = comfortable,
            Confidence = Confidence,
            LastReviewedOn = today,
            ReviewByDate = ReviewByDate is { } review ? DateOnly.FromDateTime(review) : today.AddMonths(6),
            Notes = Blank(Notes),
            UserSelectedAmount = AmountParsing.TryParseMoney(Blank(UserSelected) ?? string.Empty, out var chosen)
                ? chosen
                : null
        };

        SelectedRecordId = record.Id;
        var document = _session.Document;

        var records = document.CostGuidance
            .Where(existing => existing.Id != record.Id)
            .Append(record)
            .ToList();

        if (!_session.TryReplace(document with { CostGuidance = records }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;

        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? $"{record.Category} guidance saved from {record.SourceName}."
            : _session.LastError ?? "The guidance could not be saved.";

        if (StatusMessage?.StartsWith(record.Category, StringComparison.Ordinal) == true)
        {
            DismissEditor();
        }
    }

    [RelayCommand]
    private async Task RemoveRecordAsync(GuidanceRow? row, CancellationToken cancellationToken)
    {
        if (row is null || !_session.IsOpen)
        {
            return;
        }

        if (!_dialog.Confirm("Remove guidance", $"Remove the {row.Category} guidance from {row.Source}?"))
        {
            return;
        }

        var document = _session.Document;
        var records = document.CostGuidance.Where(record => record.Id != row.Id).ToList();

        if (!_session.TryReplace(document with { CostGuidance = records }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;

        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? "Guidance removed."
            : _session.LastError ?? "The guidance could not be removed.";
    }

    [RelayCommand]
    private async Task AddPlaceholdersAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        var document = _session.Document;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

        var missing = GroceryPlanner.DefaultCategories
            .Select(entry => entry.Name)
            .Where(name => !document.CostGuidance.Any(record =>
                string.Equals(record.Category, name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missing.Count == 0)
        {
            StatusMessage = "Every default grocery category already has a record.";
            return;
        }

        var placeholders = CostGuidanceLibrary.CreatePlaceholders(
            document.Locality,
            document.Composition,
            missing,
            today);

        var records = document.CostGuidance.Concat(placeholders).ToList();

        if (!_session.TryReplace(document with { CostGuidance = records }, out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;

        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? $"{placeholders.Count} placeholders added. Each one reads as missing information until you enter a figure."
            : _session.LastError ?? "The placeholders could not be saved.";
    }

    [RelayCommand]
    private async Task ImportStarterPackAsync(CancellationToken cancellationToken)
    {
        if (!_session.IsOpen)
        {
            ErrorMessage = "Open the household database first.";
            return;
        }

        var document = _session.Document;
        var added = StarterGuidancePack.ImportMissing(document.CostGuidance);

        if (added.Count == 0)
        {
            StatusMessage =
                $"Starter guidance pack {StarterGuidancePack.Version} is already present for the " +
                "categories on file. Household overrides were left unchanged.";
            return;
        }

        if (!_session.TryReplace(
                document with
                {
                    CostGuidance = document.CostGuidance.Concat(added).ToList(),
                    ImportedGuidancePackVersion = StarterGuidancePack.Version
                },
                out var error))
        {
            ErrorMessage = error;
            return;
        }

        ErrorMessage = null;
        StatusMessage = await _session.SaveAsync(cancellationToken)
            ? $"{added.Count} starter-pack records imported ({StarterGuidancePack.Version}). " +
              "Not permanently current. A future update must be imported explicitly. " +
              "Nothing was uploaded."
            : _session.LastError ?? "The starter pack could not be imported.";
    }

    private void ApplyRecord(CostGuidanceRecord record)
    {
        Category = record.Category;
        RecordState = record.Locality.State ?? string.Empty;
        RecordCounty = record.Locality.County ?? string.Empty;
        RecordCity = record.Locality.City ?? string.Empty;
        Adults = record.Composition.Adults.ToString();
        Children = record.Composition.Children.ToString();
        Low = AmountParsing.Format(record.Low);
        Typical = AmountParsing.Format(record.Typical);
        Comfortable = AmountParsing.Format(record.Comfortable);
        SourceName = record.SourceName;
        SourceType = record.SourceType;
        Confidence = record.Confidence;
        ObservedOn = record.ObservedOn?.ToDateTime(TimeOnly.MinValue);
        EffectiveDate = record.EffectiveDate.ToDateTime(TimeOnly.MinValue);
        ReviewByDate = record.ReviewByDate?.ToDateTime(TimeOnly.MinValue);
        Notes = record.Notes ?? string.Empty;
        UserSelected = record.UserSelectedAmount is { } chosen
            ? AmountParsing.Format(chosen)
            : string.Empty;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!_session.IsOpen)
        {
            Records = [];
            ReviewWarnings = [];
            LocalitySummary = string.Empty;
            ReviewSummary = string.Empty;
            HouseholdState = string.Empty;
            HouseholdCounty = string.Empty;
            HouseholdCity = string.Empty;
            Category = string.Empty;
            RecordState = string.Empty;
            RecordCounty = string.Empty;
            RecordCity = string.Empty;
            Low = string.Empty;
            Typical = string.Empty;
            Comfortable = string.Empty;
            SourceName = string.Empty;
            Notes = string.Empty;
            UserSelected = string.Empty;
            PackSummary = string.Empty;
            ObservedOn = null;
            EffectiveDate = null;
            ReviewByDate = null;
            SelectedRecord = null;
            SelectedRecordId = null;
            StatusMessage = null;
            ErrorMessage = null;
            DismissEditor();
            Notify();
            return;
        }

        var document = _session.Document;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

        HouseholdState = document.Locality.State ?? string.Empty;
        HouseholdCounty = document.Locality.County ?? string.Empty;
        HouseholdCity = document.Locality.City ?? string.Empty;

        LocalitySummary =
            $"Guidance is matched to {document.Locality.Describe()} for a household of " +
            $"{document.Composition.Describe()}. A record for a wider area is used when nothing " +
            "more specific exists.";

        Records = document.CostGuidance
            .OrderBy(record => record.Category, StringComparer.CurrentCulture)
            .ThenByDescending(record => record.EffectiveDate)
            .Select(record => new GuidanceRow(
                record.Id,
                record.Category,
                record.Locality.Describe(),
                record.Composition.Describe(),
                record.HasAmounts
                    ? $"{record.Low.ToDisplayString()} low · {record.Typical.ToDisplayString()} typical · " +
                      $"{record.Comfortable.ToDisplayString()} comfortable"
                    : CostGuidanceLookup.NoLocalInformation,
                record.SourceName,
                record.SourceType.ToDisplayName(),
                record.ObservedOn?.ToString("yyyy-MM-dd") ?? "Not recorded",
                record.EffectiveDate.ToString("yyyy-MM-dd"),
                record.Confidence.ToString(),
                record.LastReviewedOn?.ToString("yyyy-MM-dd") ?? "Never",
                record.ReviewByDate?.ToString("yyyy-MM-dd") ?? "No review date",
                record.RequiresReview(today) ? "Review required" : record.FreshnessLabel(today),
                record.PackVersion ?? "Household record",
                record.UserSelectedAmount?.ToDisplayString() ?? "Not chosen",
                record.DifferenceFromGuidance is { } difference
                    ? difference.ToDisplayString()
                    : "—",
                record.Notes ?? string.Empty,
                record.Id == SelectedRecordId))
            .ToList();

        SelectedRecord = Records.FirstOrDefault(item => item.Id == SelectedRecordId);

        PackSummary =
            $"Bundled pack {StarterGuidancePack.Version}, effective {StarterGuidancePack.EffectiveDate:yyyy-MM-dd}, " +
            $"review by {StarterGuidancePack.ReviewByDate:yyyy-MM-dd}. " +
            (document.ImportedGuidancePackVersion is { } imported
                ? $"Imported version {imported}."
                : "Not yet imported into this household.") +
            " Bundled guidance is not permanently current. A later pack is imported only when you choose to.";

        var stale = CostGuidanceLibrary.NeedingReview(document.CostGuidance, today);

        ReviewWarnings = stale
            .Select(record => record.HasAmounts
                ? $"{record.Category} guidance from {record.SourceName} passed its review date of " +
                  $"{record.ReviewByDate:yyyy-MM-dd}. It is outdated and requires review."
                : $"{record.Category} has no amounts recorded, so it reads as " +
                  $"\"{CostGuidanceLookup.NoLocalInformation}\".")
            .ToList();

        ReviewSummary = stale.Count == 0
            ? $"All {Records.Count} records are within their review dates."
            : $"{stale.Count} of {Records.Count} records require review. They stay visible for " +
              "reference but are not treated as current figures.";

        Notify();
    }

    private void RematchSelection()
    {
        Records = Records
            .Select(item => item with { IsSelected = item.Id == SelectedRecordId })
            .ToList();
        SelectedRecord = Records.FirstOrDefault(item => item.Id == SelectedRecordId);
        OnPropertyChanged(nameof(Records));
        OnPropertyChanged(nameof(HasRecords));
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Records));
        OnPropertyChanged(nameof(ReviewWarnings));
        OnPropertyChanged(nameof(HasRecords));
    }
}
