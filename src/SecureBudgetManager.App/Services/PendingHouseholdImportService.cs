using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Import;
using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.App.Services;

public interface IPendingHouseholdImport
{
    bool TryInspect(out PendingImportOffer? offer);

    /// <summary>
    /// Applies a payload the user has already previewed and confirmed. Never called automatically
    /// on unlock.
    /// </summary>
    Task<string?> ApplyConfirmedAsync(bool acceptConflicts, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional user-directed household import from a file outside the repository. The local
/// database is written only after an explicit confirmation.
/// </summary>
public sealed class PendingHouseholdImportService : IPendingHouseholdImport
{
    public const string ImportFolderName = "SecureBudgetManager-import";
    public const string PendingFileName = "pending-household-import.json";
    public const string ReportFileName = "last-import-report.json";

    private readonly IBudgetSession _session;
    private readonly IBudgetRepository _repository;
    private readonly ISecureBackupService _backups;
    private readonly ILocalDataDirectory _directory;
    private readonly TimeProvider _clock;
    private readonly ILogger<PendingHouseholdImportService> _logger;

    public PendingHouseholdImportService(
        IBudgetSession session,
        IBudgetRepository repository,
        ISecureBackupService backups,
        ILocalDataDirectory directory,
        TimeProvider clock,
        ILogger<PendingHouseholdImportService> logger)
    {
        _session = session;
        _repository = repository;
        _backups = backups;
        _directory = directory;
        _clock = clock;
        _logger = logger;
    }

    public static string PendingFilePath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, ImportFolderName, PendingFileName);
        }
    }

    public string ReportFilePath => Path.Combine(_directory.GetRootPath(), ReportFileName);

    public bool TryInspect(out PendingImportOffer? offer)
    {
        offer = null;
        if (!_session.IsOpen || !File.Exists(PendingFilePath))
        {
            return false;
        }

        try
        {
            var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
            offer = PendingHouseholdImportInspector.Inspect(PendingFilePath, _session.Document, today);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or IOException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(
                "Pending household import could not be previewed. Exception type: {ExceptionType}.",
                exception.GetType().Name);
            return false;
        }
    }

    public async Task<string?> ApplyConfirmedAsync(bool acceptConflicts, CancellationToken cancellationToken = default)
    {
        if (!_session.IsOpen)
        {
            return "Open the household database before importing.";
        }

        var pendingPath = PendingFilePath;
        if (!File.Exists(pendingPath))
        {
            return "There is no pending household import.";
        }

        try
        {
            var json = await File.ReadAllTextAsync(pendingPath, cancellationToken).ConfigureAwait(true);
            var payload = HouseholdImportSerializer.Parse(json);
            var incoming = payload.ToDocument();
            var merge = HouseholdImportMerger.Merge(_session.Document, incoming);

            if (merge.Preview.HasMaterialConflicts && !acceptConflicts)
            {
                _logger.LogInformation("Pending household import was not applied because conflicts were not accepted.");
                return "Household import was not applied because existing records conflict.";
            }

            var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
            var verification = HouseholdImportVerifier.Verify(
                merge.Document,
                merge.Preview,
                payload.Verification,
                today);

            if (!verification.AllPassed)
            {
                _logger.LogWarning(
                    "Pending household import failed verification. Passed {Passed} of {Total} checks.",
                    verification.Checks.Count(check => check.Passed),
                    verification.Checks.Count);
                return "Household import was not saved because verification failed.";
            }

            var previous = _session.Document;
            if (_repository.HasData())
            {
                await _backups.CreateBackupAsync(
                    _directory.GetDefaultBackupDirectory(),
                    cancellationToken).ConfigureAwait(true);
            }

            if (!_session.TryReplace(merge.Document, out var error))
            {
                return error ?? "The imported household data did not pass validation.";
            }

            if (!await _session.SaveAsync(cancellationToken).ConfigureAwait(true))
            {
                _ = _session.TryReplace(previous, out _);
                return _session.LastError ?? "The imported household data could not be saved.";
            }

            var reloaded = _repository.Load();
            var reloadMerge = HouseholdImportMerger.Merge(reloaded, incoming);
            var reloadVerification = HouseholdImportVerifier.Verify(
                reloaded,
                reloadMerge.Preview,
                payload.Verification,
                today);

            if (!reloadVerification.AllPassed)
            {
                _ = _session.TryReplace(previous, out _);
                await _session.SaveAsync(cancellationToken).ConfigureAwait(true);
                return "Household import was rolled back because the reloaded document failed verification.";
            }

            if (!_session.TryReplace(reloaded, out error))
            {
                return error ?? "The imported household data could not be reloaded.";
            }

            _directory.EnsureCreated();
            var reportJson = JsonSerializer.Serialize(
                new
                {
                    appliedUtc = DateTime.UtcNow,
                    created = merge.Preview.Created.Count,
                    updated = merge.Preview.Updated.Count,
                    unchanged = merge.Preview.Unchanged.Count,
                    conflicts = merge.Preview.Conflicts.Count,
                    checksPassed = reloadVerification.Checks.Count(check => check.Passed),
                    checksTotal = reloadVerification.Checks.Count,
                    payloadRemoved = true
                },
                HouseholdImportSerializer.Options);
            await File.WriteAllTextAsync(ReportFilePath, reportJson, cancellationToken).ConfigureAwait(true);

            File.Delete(pendingPath);
            var removed = !File.Exists(pendingPath);
            var importDirectory = Path.GetDirectoryName(pendingPath);
            if (removed && importDirectory is not null && Directory.Exists(importDirectory)
                && !Directory.EnumerateFileSystemEntries(importDirectory).Any())
            {
                Directory.Delete(importDirectory);
            }

            _logger.LogInformation(
                "Pending household import saved. Created {Created}, updated {Updated}, unchanged {Unchanged}. Payload removed: {Removed}.",
                merge.Preview.Created.Count,
                merge.Preview.Updated.Count,
                merge.Preview.Unchanged.Count,
                removed);

            return removed
                ? $"Household import saved ({merge.Preview.Summary}). The temporary import file was removed."
                : $"Household import saved ({merge.Preview.Summary}). Remove the temporary import file manually.";
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or IOException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(
                "Pending household import failed. Exception type: {ExceptionType}.",
                exception.GetType().Name);
            return exception is ArgumentException
                ? exception.Message
                : "The pending household import could not be applied.";
        }
    }
}
