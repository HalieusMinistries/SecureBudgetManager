using System.Windows;
using System.Windows.Controls;

namespace SecureBudgetManager.App.Interaction;

/// <summary>
/// Keeps a page ScrollViewer offset in the view-model so it can be restored after an editor closes.
/// </summary>
public static class ScrollRestore
{
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "VerticalOffset",
            typeof(double),
            typeof(ScrollRestore),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVerticalOffsetChanged));

    private static readonly DependencyProperty IsHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsHooked",
            typeof(bool),
            typeof(ScrollRestore),
            new PropertyMetadata(false));

    public static double GetVerticalOffset(DependencyObject target) =>
        (double)target.GetValue(VerticalOffsetProperty);

    public static void SetVerticalOffset(DependencyObject target, double value) =>
        target.SetValue(VerticalOffsetProperty, value);

    private static void OnVerticalOffsetChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollViewer viewer)
        {
            return;
        }

        if (!(bool)viewer.GetValue(IsHookedProperty))
        {
            viewer.SetValue(IsHookedProperty, true);
            viewer.ScrollChanged += (_, change) =>
            {
                if (Math.Abs(change.VerticalOffset - GetVerticalOffset(viewer)) > 0.5)
                {
                    SetVerticalOffset(viewer, change.VerticalOffset);
                }
            };
        }

        var desired = (double)args.NewValue;
        if (Math.Abs(viewer.VerticalOffset - desired) > 0.5)
        {
            viewer.ScrollToVerticalOffset(desired);
        }
    }
}
