using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SecureBudgetManager.App.Views;

public partial class WorkspaceView
{
    public WorkspaceView()
    {
        InitializeComponent();
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
