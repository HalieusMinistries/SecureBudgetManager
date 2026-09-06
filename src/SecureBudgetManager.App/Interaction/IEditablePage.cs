using System.Windows.Input;

namespace SecureBudgetManager.App.Interaction;

/// <summary>
/// A workspace page that can open a focused record editor in the shared overlay.
/// </summary>
public interface IEditablePage
{
    bool IsEditorOpen { get; }

    string EditorTitle { get; }

    string EditorSaveLabel { get; }

    string? EditorEffectPreview { get; }

    bool HasEditorChanges { get; }

    bool HasEditorError { get; }

    string? EditorError { get; }

    IReadOnlyDictionary<string, string> FieldErrors => EditorSaveResult.NoFieldErrors;

    string? FocusField => null;

    bool IsEditorSaving => false;

    ICommand SaveEditorCommand { get; }

    ICommand CancelEditorCommand { get; }

    /// <summary>Returns false when the user keeps the editor open.</summary>
    bool TryLeaveEditor();

    /// <summary>Closes the editor without a prompt. Used when Privacy hides the workspace.</summary>
    void DismissEditor();
}
