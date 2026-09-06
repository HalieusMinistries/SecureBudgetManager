using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;

namespace SecureBudgetManager.App.Capture;

/// <summary>
/// Covers the live page so internal scrolling during capture is not visible.
/// </summary>
internal sealed class FreezeFrameAdorner : Adorner
{
    private readonly Image _image;

    public FreezeFrameAdorner(UIElement adornedElement, BitmapSource snapshot)
        : base(adornedElement)
    {
        IsHitTestVisible = true;
        _image = new Image
        {
            Source = snapshot,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true
        };
        AddVisualChild(_image);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _image;

    protected override Size MeasureOverride(Size constraint)
    {
        _image.Measure(constraint);
        return _image.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _image.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
