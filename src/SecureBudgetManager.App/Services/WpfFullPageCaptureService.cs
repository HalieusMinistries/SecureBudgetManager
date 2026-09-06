using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SecureBudgetManager.App.Capture;
using SecureBudgetManager.Core.Capture;

namespace SecureBudgetManager.App.Services;

public sealed class WpfFullPageCaptureService : IFullPageCaptureService
{
    public const int MaximumPixelEdge = 8192;

    private readonly ILogger<WpfFullPageCaptureService> _logger;

    public WpfFullPageCaptureService(ILogger<WpfFullPageCaptureService> logger)
    {
        _logger = logger;
    }

    public FullPageCaptureResult Capture(FrameworkElement page, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (Path.GetExtension(destinationPath).Length == 0)
        {
            destinationPath += ".png";
        }

        FreezeFrameAdorner? overlay = null;
        AdornerLayer? layer = null;
        var focused = KeyboardFocus(page);
        var written = new List<string>();
        ScrollCaptureSession? session = null;
        ScrollViewer? primary = null;
        object? savedContent = null;
        Border? host = null;
        var nestedHeights = new List<(ScrollViewer Viewer, double Height)>();

        try
        {
            page.UpdateLayout();
            var freeze = RenderVisual(page, page.ActualWidth, page.ActualHeight);
            layer = AdornerLayer.GetAdornerLayer(page) ?? AdornerLayer.GetAdornerLayer(Window.GetWindow(page));
            if (layer is not null && freeze is not null)
            {
                overlay = new FreezeFrameAdorner(page, freeze);
                layer.Add(overlay);
                page.UpdateLayout();
            }

            session = new ScrollCaptureSession();
            var scrollViewers = FindScrollViewers(page).ToList();
            foreach (var viewer in scrollViewers)
            {
                session.Remember(new WpfScrollSurface(viewer));
            }

            primary = scrollViewers.FirstOrDefault() ?? FindPrimaryScrollViewer(page);
            var content = ResolveContent(page, primary);
            var width = ResolveContentWidth(page, primary, content);
            var background = ResolvePageBackground(page);
            var dpi = VisualTreeHelper.GetDpi(page);

            if (primary is not null && ReferenceEquals(primary.Content, content))
            {
                savedContent = primary.Content;
                primary.Content = null;
                host = new Border
                {
                    Background = background,
                    Child = content,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = true
                };
                ExpandNestedScrollViewers(host, width, nestedHeights);
                host.Measure(new Size(width, double.PositiveInfinity));
                var contentWidth = Math.Max(width, host.DesiredSize.Width);
                var contentHeight = Math.Max(host.DesiredSize.Height, 1);
                host.Measure(new Size(contentWidth, contentHeight));
                host.Arrange(new Rect(0, 0, contentWidth, contentHeight));
                host.Width = contentWidth;
                host.Height = contentHeight;
                host.UpdateLayout();

                var maxTileDip = MaximumPixelEdge / Math.Max(dpi.DpiScaleY, 0.5);
                var layout = FullPageCapturePlanner.Create(
                    contentWidth,
                    contentHeight,
                    verticalScrollOffset: 0,
                    viewportHeight: 0,
                    maxTileDip);

                if (layout.Segments.Count == 0)
                {
                    return new FullPageCaptureResult(false, 0, "The page has no visible content to capture.");
                }

                var paths = CaptureFileName.SegmentPaths(destinationPath, layout.FileCount);
                if (ConflictsExist(paths, destinationPath))
                {
                    return new FullPageCaptureResult(false, 0, "A capture file with that name already exists.");
                }

                WriteSegments(host, background, layout, paths, dpi, written);
            }
            else
            {
                page.Measure(new Size(Math.Max(page.ActualWidth, 1), double.PositiveInfinity));
                var contentWidth = Math.Max(page.ActualWidth, page.DesiredSize.Width);
                var contentHeight = Math.Max(page.DesiredSize.Height, page.ActualHeight);
                var maxTileDip = MaximumPixelEdge / Math.Max(dpi.DpiScaleY, 0.5);
                var layout = FullPageCapturePlanner.Create(
                    contentWidth,
                    contentHeight,
                    verticalScrollOffset: 0,
                    viewportHeight: 0,
                    maxTileDip);

                if (layout.Segments.Count == 0)
                {
                    return new FullPageCaptureResult(false, 0, "The page has no visible content to capture.");
                }

                var paths = CaptureFileName.SegmentPaths(destinationPath, layout.FileCount);
                if (ConflictsExist(paths, destinationPath))
                {
                    return new FullPageCaptureResult(false, 0, "A capture file with that name already exists.");
                }

                WriteSegments(page, background, layout, paths, dpi, written);
            }
            _logger.LogInformation("Full-page capture wrote {FileCount} PNG file(s).", written.Count);
            return new FullPageCaptureResult(true, written.Count, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            DeleteWritten(written);
            _logger.LogError("Full-page capture failed. Exception type: {ExceptionType}.", exception.GetType().Name);
            return new FullPageCaptureResult(false, 0, "The page could not be captured.");
        }
        finally
        {
            if (host is not null)
            {
                host.Child = null;
            }

            foreach (var (viewer, height) in nestedHeights)
            {
                viewer.Height = height;
            }

            if (primary is not null && savedContent is not null)
            {
                primary.Content = savedContent;
            }

            session?.Dispose();

            if (overlay is not null && layer is not null)
            {
                layer.Remove(overlay);
            }

            RestoreFocus(focused);
            page.UpdateLayout();
        }
    }

    private static void WriteSegments(
        FrameworkElement host,
        Brush background,
        FullPageCaptureLayout layout,
        IReadOnlyList<string> paths,
        DpiScale dpi,
        List<string> written)
    {
        for (var i = 0; i < layout.Segments.Count; i++)
        {
            var segment = layout.Segments[i];
            BitmapSource? bitmap;
            if (layout.FileCount == 1 && layout.OriginY == 0 && segment.Offset == 0)
            {
                bitmap = RenderVisual(host, layout.ContentWidth, segment.Height, dpi);
            }
            else
            {
                var source = new Rect(layout.OriginX, layout.OriginY + segment.Offset, layout.ContentWidth, segment.Height);
                bitmap = RenderSlice(host, background, source, dpi);
            }
            if (bitmap is null)
            {
                throw new InvalidOperationException("The page visual could not be rendered.");
            }

            EncodePng(bitmap, paths[i]);
            written.Add(paths[i]);
        }
    }

    private static FrameworkElement ResolveContent(FrameworkElement page, ScrollViewer? primary) =>
        primary?.Content as FrameworkElement ?? page;

    private static double ResolveContentWidth(FrameworkElement page, ScrollViewer? primary, FrameworkElement content)
    {
        if (primary is not null && primary.ViewportWidth > 1)
        {
            return primary.ViewportWidth;
        }

        if (content.ActualWidth > 1)
        {
            return content.ActualWidth;
        }

        return Math.Max(page.ActualWidth, 1);
    }

    private static Brush ResolvePageBackground(FrameworkElement page)
    {
        if (page.TryFindResource("Brush.Window") is Brush themed)
        {
            return themed;
        }

        if (Window.GetWindow(page)?.Background is Brush windowBrush)
        {
            return windowBrush;
        }

        if (page is Control control && control.Background is Brush controlBrush)
        {
            return controlBrush;
        }

        if (page is Border border && border.Background is Brush borderBrush)
        {
            return borderBrush;
        }

        if (page is Panel panel && panel.Background is Brush panelBrush)
        {
            return panelBrush;
        }

        return new SolidColorBrush(Color.FromRgb(0x07, 0x0F, 0x1C));
    }

    private static void ExpandNestedScrollViewers(
        DependencyObject root,
        double width,
        List<(ScrollViewer Viewer, double Height)> nestedHeights)
    {
        foreach (var viewer in FindScrollViewers(root))
        {
            if (IsInsideUtilityControl(viewer))
            {
                continue;
            }

            if (viewer.Content is not FrameworkElement inner)
            {
                continue;
            }

            nestedHeights.Add((viewer, viewer.Height));
            var constraint = viewer.ViewportWidth > 1 ? viewer.ViewportWidth : Math.Max(width, 1);
            inner.Measure(new Size(constraint, double.PositiveInfinity));
            var needed = inner.DesiredSize.Height;
            if (needed > 1)
            {
                viewer.Height = needed;
            }
        }
    }

    private static bool IsInsideUtilityControl(DependencyObject node)
    {
        DependencyObject? current = node;
        while (current is not null)
        {
            if (current is ComboBox or DatePicker or Calendar or TextBox)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static bool ConflictsExist(IReadOnlyList<string> paths, string chosenPath)
    {
        foreach (var path in paths)
        {
            if (string.Equals(path, chosenPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(path))
            {
                return true;
            }
        }

        return false;
    }

    private static ScrollViewer? FindPrimaryScrollViewer(DependencyObject root) =>
        FindScrollViewers(root).FirstOrDefault();

    private static IEnumerable<ScrollViewer> FindScrollViewers(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                yield return viewer;
            }

            foreach (var nested in FindScrollViewers(child))
            {
                yield return nested;
            }
        }
    }

    private static BitmapSource? RenderVisual(Visual visual, double dipWidth, double dipHeight, DpiScale? dpi = null)
    {
        if (dipWidth < 1 || dipHeight < 1)
        {
            return null;
        }

        var scale = dpi ?? VisualTreeHelper.GetDpi(visual);
        return RenderPrepared(
            visual,
            Math.Max(1, (int)Math.Ceiling(dipWidth * scale.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(dipHeight * scale.DpiScaleY)),
            scale);
    }

    private static BitmapSource? RenderSlice(Visual visual, Brush background, Rect sourceDip, DpiScale dpi)
    {
        if (sourceDip.Width < 1 || sourceDip.Height < 1)
        {
            return null;
        }

        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var dest = new Rect(0, 0, sourceDip.Width, sourceDip.Height);
            context.DrawRectangle(background, null, dest);
            var brush = new VisualBrush(visual)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                Viewbox = sourceDip,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = dest,
                ViewportUnits = BrushMappingMode.Absolute
            };
            context.DrawRectangle(brush, null, dest);
        }

        return RenderPrepared(
            drawing,
            Math.Max(1, (int)Math.Ceiling(sourceDip.Width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(sourceDip.Height * dpi.DpiScaleY)),
            dpi);
    }

    private static BitmapSource RenderPrepared(Visual visual, int pixelWidth, int pixelHeight, DpiScale dpi)
    {
        pixelWidth = Math.Min(pixelWidth, MaximumPixelEdge);
        pixelHeight = Math.Min(pixelHeight, MaximumPixelEdge);

        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);

        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void EncodePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static void DeleteWritten(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static IInputElement? KeyboardFocus(DependencyObject page) =>
        FocusManager.GetFocusedElement(Window.GetWindow(page) ?? page);

    private static void RestoreFocus(IInputElement? focused)
    {
        if (focused is UIElement element)
        {
            element.Focus();
        }
    }
}
