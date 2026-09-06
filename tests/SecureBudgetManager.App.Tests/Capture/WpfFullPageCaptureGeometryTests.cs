using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using SecureBudgetManager.App.Services;

namespace SecureBudgetManager.App.Tests.Capture;

[Collection("StaWpf")]
public sealed class WpfFullPageCaptureGeometryTests
{
    private static readonly Color PageBackground = Color.FromRgb(0x07, 0x0F, 0x1C);
    private static readonly Color Heading = Color.FromRgb(0xE5, 0x73, 0x73);
    private static readonly Color Middle = Color.FromRgb(0x1C, 0x3F, 0x63);
    private static readonly Color Footer = Color.FromRgb(0x7E, 0xC2, 0xFF);

    [Fact]
    public void Capture_StartsAtContentTop_IgnoresScroll_CropsToContent_UsesPageBackground()
    {
        Sta.Run(() =>
        {
            var folder = CreateFolder();
            Window? window = null;
            try
            {
                var page = CreateScrolledPage(out var viewer, out var contentHeight);
                window = Host(page);
                viewer.ScrollToVerticalOffset(420);
                window.UpdateLayout();
                Assert.True(viewer.VerticalOffset > 100);

                var destination = Path.Combine(folder, "page.png");
                var service = new WpfFullPageCaptureService(NullLogger<WpfFullPageCaptureService>.Instance);
                var result = service.Capture(page, destination);

                Assert.True(result.Succeeded, result.Error);
                Assert.Equal(1, result.FilesWritten);
                Assert.True(File.Exists(destination));
                Assert.InRange(viewer.VerticalOffset, 419, 421);

                var bitmap = LoadPng(destination);
                var dpi = VisualTreeHelper.GetDpi(page);
                var expectedHeight = (int)Math.Ceiling(contentHeight * dpi.DpiScaleY);
                Assert.InRange(bitmap.PixelHeight, expectedHeight - 2, expectedHeight + 2);

                Assert.True(IsNear(GetPixel(bitmap, 8, 4), Heading), "Capture must start at the heading, not the scrolled middle.");
                Assert.False(IsNear(GetPixel(bitmap, 8, 4), Middle));

                var bottom = GetPixel(bitmap, 8, bitmap.PixelHeight - 4);
                Assert.True(IsNear(bottom, Footer) || IsNear(bottom, PageBackground), "The last rows must be page content, not a black pad.");
                Assert.False(IsNear(bottom, Colors.Black) && bottom.R < 8 && bottom.G < 8 && bottom.B < 8);

                var trailingBlack = CountNearBlack(bitmap, bitmap.PixelHeight - 24, 24);
                Assert.True(trailingBlack < bitmap.PixelWidth, "Do not append a black unused region.");

                var sampleBackground = GetPixel(bitmap, bitmap.PixelWidth / 2, 50);
                Assert.False(sampleBackground.R == 0 && sampleBackground.G == 0 && sampleBackground.B == 0 && sampleBackground.A > 200);
            }
            finally
            {
                window?.Close();
                Directory.Delete(folder, recursive: true);
            }
        });
    }

    [Fact]
    public void Capture_RestoresScrollOffsetAfterFailure()
    {
        Sta.Run(() =>
        {
            var folder = CreateFolder();
            Window? window = null;
            try
            {
                var page = CreateScrolledPage(out var viewer, out _);
                window = Host(page);
                viewer.ScrollToVerticalOffset(280);
                window.UpdateLayout();

                var service = new WpfFullPageCaptureService(NullLogger<WpfFullPageCaptureService>.Instance);
                var missing = Path.Combine(folder, "missing-directory", "out.png");
                var result = service.Capture(page, missing);

                Assert.False(result.Succeeded);
                Assert.InRange(viewer.VerticalOffset, 279, 281);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        });
    }

    private static Border CreateScrolledPage(out ScrollViewer viewer, out double contentHeight)
    {
        const double heading = 48;
        const double spacer = 960;
        const double footer = 48;
        contentHeight = heading + spacer + footer;

        var stack = new StackPanel();
        stack.Children.Add(Band(heading, Heading));
        stack.Children.Add(Band(spacer, Middle));
        stack.Children.Add(Band(footer, Footer));

        viewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack
        };

        return new Border
        {
            Background = new SolidColorBrush(PageBackground),
            Child = viewer
        };
    }

    private static Window Host(FrameworkElement page)
    {
        var windowBrush = new SolidColorBrush(PageBackground);
        var window = new Window
        {
            Width = 420,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -16000,
            Top = -16000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow,
            Background = windowBrush,
            Content = page
        };
        window.Resources["Brush.Window"] = windowBrush;
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static System.Windows.Shapes.Rectangle Band(double height, Color color) =>
        new()
        {
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Fill = new SolidColorBrush(color)
        };

    private static BitmapSource LoadPng(string path)
    {
        var image = new BitmapImage();
        using var stream = File.OpenRead(path);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static Color GetPixel(BitmapSource bitmap, int x, int y)
    {
        x = Math.Clamp(x, 0, bitmap.PixelWidth - 1);
        y = Math.Clamp(y, 0, bitmap.PixelHeight - 1);
        var cropped = new CroppedBitmap(bitmap, new Int32Rect(x, y, 1, 1));
        var converted = new FormatConvertedBitmap(cropped, PixelFormats.Pbgra32, null, 0);
        var bytes = new byte[4];
        converted.CopyPixels(bytes, 4, 0);
        return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }

    private static int CountNearBlack(BitmapSource bitmap, int startY, int rows)
    {
        var count = 0;
        for (var y = startY; y < startY + rows && y < bitmap.PixelHeight; y++)
        {
            for (var x = 0; x < bitmap.PixelWidth; x += 8)
            {
                var pixel = GetPixel(bitmap, x, y);
                if (pixel.R < 10 && pixel.G < 10 && pixel.B < 10 && pixel.A > 200)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool IsNear(Color actual, Color expected) =>
        Math.Abs(actual.R - expected.R) < 40
        && Math.Abs(actual.G - expected.G) < 40
        && Math.Abs(actual.B - expected.B) < 40;

    private static string CreateFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sbm-capture-geometry", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
