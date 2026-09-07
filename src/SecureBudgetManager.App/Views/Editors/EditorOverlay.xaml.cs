using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SecureBudgetManager.App.Views.Editors;

public partial class EditorOverlay : UserControl
{
    public EditorOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshFormReadiness();
        DataContextChanged += (_, _) => Dispatcher.BeginInvoke(RefreshFormReadiness);
    }

    public bool IsEditorFormReady { get; private set; }

    public FrameworkElement? FindEditorForm()
    {
        EditorBodyHost.ApplyTemplate();
        return FindEditorForm(EditorBodyHost);
    }

    public bool RefreshFormReadiness()
    {
        UpdateLayout();
        var form = FindEditorForm();
        IsEditorFormReady = form is not null
                            && form.IsVisible
                            && form.ActualHeight > 1
                            && form.ActualWidth > 1
                            && HasVisibleInput(form);
        return IsEditorFormReady;
    }

    public static bool HasVisibleInput(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement { Visibility: Visibility.Visible, ActualHeight: > 1 }
                && child is TextBox or ComboBox or DatePicker or CheckBox or ItemsControl)
            {
                return true;
            }

            if (child is FrameworkElement { Visibility: Visibility.Visible } && HasVisibleInput(child))
            {
                return true;
            }
        }

        return false;
    }

    private static FrameworkElement? FindEditorForm(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is UserControl control
                && control.GetType().Name.EndsWith("EditorForm", StringComparison.Ordinal))
            {
                return control;
            }

            var nested = FindEditorForm(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
