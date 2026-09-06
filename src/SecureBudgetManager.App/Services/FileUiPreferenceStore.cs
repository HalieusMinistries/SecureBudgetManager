using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Locking;

namespace SecureBudgetManager.App.Services;

public sealed class FileUiPreferenceStore : IUiPreferenceStore
{
    private const string FileName = "ui-preferences.json";
    private readonly ILocalDataDirectory _directory;
    private readonly ILogger<FileUiPreferenceStore> _logger;
    private StoreDto? _cache;

    public FileUiPreferenceStore(ILocalDataDirectory directory, ILogger<FileUiPreferenceStore> logger)
    {
        _directory = directory;
        _logger = logger;
    }

    public bool FullPageCaptureWarningAcknowledged => Load().FullPageCaptureWarningAcknowledged;

    public int LockTimeoutMinutes
    {
        get
        {
            var minutes = Load().LockTimeoutMinutes;
            return minutes <= 0 ? (int)InactivityLockPolicy.DefaultTimeout.TotalMinutes : minutes;
        }
    }

    public void AcknowledgeFullPageCaptureWarning()
    {
        var dto = Load();
        dto.FullPageCaptureWarningAcknowledged = true;
        Write(dto);
    }

    public void SetLockTimeoutMinutes(int minutes)
    {
        var dto = Load();
        dto.LockTimeoutMinutes = (int)InactivityLockPolicy.Clamp(TimeSpan.FromMinutes(minutes)).TotalMinutes;
        Write(dto);
    }

    private StoreDto Load()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        try
        {
            var path = Path.Combine(_directory.GetRootPath(), FileName);
            if (!File.Exists(path))
            {
                _cache = new StoreDto();
                return _cache;
            }

            _cache = JsonSerializer.Deserialize<StoreDto>(File.ReadAllText(path)) ?? new StoreDto();
            return _cache;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning("A UI preference could not be read. Exception type: {ExceptionType}.", exception.GetType().Name);
            _cache = new StoreDto();
            return _cache;
        }
    }

    private void Write(StoreDto dto)
    {
        _cache = dto;
        try
        {
            _directory.EnsureCreated();
            var path = Path.Combine(_directory.GetRootPath(), FileName);
            File.WriteAllText(path, JsonSerializer.Serialize(dto));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("A UI preference could not be stored. Exception type: {ExceptionType}.", exception.GetType().Name);
        }
    }

    private sealed class StoreDto
    {
        public bool FullPageCaptureWarningAcknowledged { get; set; }

        public int LockTimeoutMinutes { get; set; }
    }
}
