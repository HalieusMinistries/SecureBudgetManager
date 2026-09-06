using Microsoft.Extensions.Logging;
using SecureBudgetManager.Core.Abstractions;
using SecureBudgetManager.Core.Security;
using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.App.Services;

public sealed class BudgetSession : IBudgetSession
{
    private readonly IBudgetRepository _repository;
    private readonly IVaultService _vault;
    private readonly ILogger<BudgetSession> _logger;
    private readonly object _gate = new();
    private BudgetDocument _document = new();
    private readonly Stack<BudgetDocument> _undo = new();
    private int _saveDepth;

    public BudgetSession(
        IBudgetRepository repository,
        IVaultService vault,
        ILogger<BudgetSession> logger)
    {
        _repository = repository;
        _vault = vault;
        _logger = logger;
    }

    public bool IsOpen { get; private set; }

    public bool HasUnsavedChanges { get; private set; }

    public bool IsSaving => Volatile.Read(ref _saveDepth) > 0;

    public bool CanUndo
    {
        get
        {
            lock (_gate)
            {
                return IsOpen && _undo.Count > 0;
            }
        }
    }

    public string? LastError { get; private set; }

    public BudgetDocument Document
    {
        get
        {
            lock (_gate)
            {
                return _document;
            }
        }
    }

    public event EventHandler? Changed;

    public bool Open()
    {
        if (_vault.Status != VaultStatus.Unlocked)
        {
            LastError = "Open the household database before loading household data.";
            return false;
        }

        try
        {
            var loaded = _repository.Load();
            lock (_gate)
            {
                _document = loaded;
                _undo.Clear();
                IsOpen = true;
                HasUnsavedChanges = false;
                LastError = null;
            }

            _logger.LogInformation("Household document loaded from the local database.");
            RaiseChanged();
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LastError = UserFacingError.From(exception);
            _logger.LogError("Household document could not be loaded. Exception type: {ExceptionType}.", exception.GetType().Name);
            return false;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _document = new BudgetDocument();
            _undo.Clear();
            IsOpen = false;
            HasUnsavedChanges = false;
            LastError = null;
        }

        RaiseChanged();
    }

    public bool TryReplace(BudgetDocument document, out string? error)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!IsOpen)
        {
            error = "Open the household database before changing household data.";
            LastError = error;
            return false;
        }

        try
        {
            document.Validate();
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            LastError = error;
            return false;
        }
        catch (Exception exception)
        {
            error = UserFacingError.From(exception);
            LastError = error;
            _logger.LogError(
                "Household document validation failed. Exception type: {ExceptionType}.",
                exception.GetType().Name);
            return false;
        }

        lock (_gate)
        {
            _undo.Push(_document);
            if (_undo.Count > 40)
            {
                var kept = _undo.Take(40).Reverse().ToArray();
                _undo.Clear();
                foreach (var item in kept)
                {
                    _undo.Push(item);
                }
            }

            _document = document;
            HasUnsavedChanges = true;
            LastError = null;
        }

        RaiseChanged();
        error = null;
        return true;
    }

    public bool Undo()
    {
        lock (_gate)
        {
            if (!IsOpen || _undo.Count == 0)
            {
                LastError = "There is nothing to undo.";
                return false;
            }

            _document = _undo.Pop();
            HasUnsavedChanges = true;
            LastError = null;
        }

        RaiseChanged();
        return true;
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
        {
            LastError = "Open the household database before saving.";
            return false;
        }

        if (Interlocked.CompareExchange(ref _saveDepth, 1, 0) != 0)
        {
            LastError = "A save is already in progress.";
            return false;
        }

        BudgetDocument snapshot;
        lock (_gate)
        {
            snapshot = _document;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Run(() => _repository.Save(snapshot), cancellationToken).ConfigureAwait(true);

            lock (_gate)
            {
                if (ReferenceEquals(_document, snapshot) || DocumentsMatch(_document, snapshot))
                {
                    HasUnsavedChanges = false;
                }

                LastError = null;
            }

            _logger.LogInformation("Household document saved to the local database.");
            RaiseChanged();
            return true;
        }
        catch (OperationCanceledException)
        {
            LastError = "The save was cancelled.";
            return false;
        }
        catch (Exception exception)
        {
            LastError = UserFacingError.From(exception);
            _logger.LogError("Household save failed. Exception type: {ExceptionType}.", exception.GetType().Name);
            RaiseChanged();
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _saveDepth, 0);
            RaiseChanged();
        }
    }

    private static bool DocumentsMatch(BudgetDocument left, BudgetDocument right) =>
        ReferenceEquals(left, right);

    private void RaiseChanged()
    {
        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (var subscriber in handlers.GetInvocationList())
        {
            try
            {
                subscriber.DynamicInvoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "A workspace page could not refresh after a document change. Exception type: {ExceptionType}.",
                    exception.GetType().Name);
            }
        }
    }
}
