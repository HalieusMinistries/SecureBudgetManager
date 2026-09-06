using SecureBudgetManager.App.Services;
using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.App.Interaction;

/// <summary>
/// Shared persist path for overlay editors: validate, write through the session, verify, notify.
/// </summary>
public static class EditorSaveCoordinator
{
    private static readonly AsyncLocal<int> Depth = new();

    public static bool IsBusy => Depth.Value > 0;

    public static async Task<EditorSaveResult> TryCommitAsync(
        IBudgetSession session,
        BudgetDocument document,
        CancellationToken cancellationToken,
        string savedMessage,
        Func<BudgetDocument, bool>? verify = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(savedMessage);

        if (session.IsSaving || Depth.Value > 0)
        {
            return EditorSaveResult.AlreadySaving();
        }

        Depth.Value = 1;
        try
        {
            if (!session.IsOpen)
            {
                return EditorSaveResult.NotSaved("Open the household database first.");
            }

            if (!session.TryReplace(document, out var error))
            {
                return EditorSaveResult.Validation(
                    string.IsNullOrWhiteSpace(error) ? "Correct the highlighted information." : error);
            }

            return await PersistReplacedAsync(session, cancellationToken, savedMessage, verify).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return EditorSaveResult.NotSaved("The save was cancelled.");
        }
        catch (Exception exception)
        {
            return EditorSaveResult.NotSaved(UserFacingError.From(exception));
        }
        finally
        {
            Depth.Value = 0;
        }
    }

    public static async Task<EditorSaveResult> PersistCurrentAsync(
        IBudgetSession session,
        CancellationToken cancellationToken,
        string savedMessage,
        Func<BudgetDocument, bool>? verify = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(savedMessage);

        if (session.IsSaving || Depth.Value > 0)
        {
            return EditorSaveResult.AlreadySaving();
        }

        Depth.Value = 1;

        try
        {
            return await PersistReplacedAsync(session, cancellationToken, savedMessage, verify).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return EditorSaveResult.NotSaved("The save was cancelled.");
        }
        catch (Exception exception)
        {
            return EditorSaveResult.NotSaved(UserFacingError.From(exception));
        }
        finally
        {
            Depth.Value = 0;
        }
    }

    private static async Task<EditorSaveResult> PersistReplacedAsync(
        IBudgetSession session,
        CancellationToken cancellationToken,
        string savedMessage,
        Func<BudgetDocument, bool>? verify)
    {
        bool persisted;
        try
        {
            persisted = await session.SaveAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return EditorSaveResult.NotSaved(UserFacingError.From(exception));
        }

        if (!persisted)
        {
            return EditorSaveResult.NotSaved(session.LastError);
        }

        if (verify is not null && !verify(session.Document))
        {
            return EditorSaveResult.NotSaved("The saved record did not match what was entered.");
        }

        WorkspaceNoticeService.Current.ShowSaved(savedMessage);
        return EditorSaveResult.Saved(savedMessage);
    }

    public static EditorSaveResult FailedToBegin() => EditorSaveResult.AlreadySaving();
}
