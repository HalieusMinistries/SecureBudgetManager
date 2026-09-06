using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SecureBudgetManager.App.ViewModels;

namespace SecureBudgetManager.App.Views;

public partial class WorkspaceView
{
    public WorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnWorkspaceDataContextChanged;
    }

    private void OnWorkspaceDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.OldValue is INotifyPropertyChanged previous)
        {
            previous.PropertyChanged -= OnWorkspacePropertyChanged;
        }

        if (args.NewValue is INotifyPropertyChanged next)
        {
            next.PropertyChanged += OnWorkspacePropertyChanged;
        }
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MainViewModel.IsEditorVisible)
            || DataContext is not MainViewModel { IsEditorVisible: false })
        {
            return;
        }

        Dispatcher.BeginInvoke(() => RestoreSelectedRowFocus(ActivePageVisual), DispatcherPriority.Input);
    }

    private static void RestoreSelectedRowFocus(DependencyObject? root)
    {
        if (root is null)
        {
            return;
        }

        var selected = FindSelectedRow(root);
        if (selected is { Focusable: true })
        {
            selected.Focus();
            return;
        }

        if (root is IInputElement page)
        {
            page.Focus();
        }
    }

    private static FrameworkElement? FindSelectedRow(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element
                && element.DataContext is { } context
                && context.GetType().GetProperty("IsSelected")?.GetValue(context) is true)
            {
                return element;
            }

            var nested = FindSelectedRow(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    public FrameworkElement? ActivePageVisual
    {
        get
        {
            return FindPageView(PageHost) ?? PageHost;
        }
    }

    private static FrameworkElement? FindPageView(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UserControl page)
            {
                return page;
            }

            var nested = FindPageView(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
