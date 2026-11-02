using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

/// <summary>
/// Renders the objective row offscreen with all three states side by side.
/// <para>
/// This exists because WPF swallows binding failures. A typo in
/// <c>FontWeight="{Binding RowWeight}"</c> does not throw, does not warn and does not fail a unit
/// test — the row simply keeps its default weight forever. The only way to know the binding is live
/// is to draw it and read the pixels back, which is what the assertion below does.
/// </para>
/// </summary>
public sealed class ObjectiveRowRenderTests
{
    [Fact]
    public void TheImminentRow_ActuallyRendersDifferentlyFromACalmOne()
    {
        const int width = 1200;
        const int height = 220;

        var (pixels, path) = OnStaThread(() =>
        {
            var viewModel = new OverlayViewModel();

            viewModel.Objectives.Add(Objective(ObjectiveKind.Dragon, "Dragon", "04:12", imminent: false));
            viewModel.Objectives.Add(Objective(ObjectiveKind.Baron, "Baron", "00:24", imminent: true));
            viewModel.Objectives.Add(Objective(ObjectiveKind.Herald, "Herald", "hazır", ready: true));

            var host = LoadObjectiveRow();
            host.DataContext = viewModel;

            // Sized explicitly, the way the real PanelHost canvas is sized by the window. Letting
            // the host size to its content instead produces a circular measure — the row's MaxWidth
            // is a fraction of the host, and a host with no width of its own then collapses to
            // zero and wraps every objective onto its own line.
            host.Width = width;
            host.Height = height;

            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();

            var visual = new DrawingVisual();

            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
                context.DrawRectangle(new VisualBrush(host), null, new Rect(0, 0, width, height));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var buffer = new byte[width * height * 4];
            bitmap.CopyPixels(buffer, width * 4, 0);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            var file = Path.Combine(Path.GetTempPath(), "riftoverlay-objectives.png");
            using (var stream = File.Create(file))
            {
                encoder.Save(stream);
            }

            return (buffer, file);
        });

        // Read the pixels back rather than the view model. A binding typo leaves the view model
        // perfectly correct and the screen unchanged, so only the rendered image can tell the two
        // apart — which is the entire reason this test exists.
        Assert.True(Contains(pixels, Palette.Imminent.Color),
            "Yaklaşan objektifin amber rengi ekrana hiç çizilmedi — StatusBrush bağlaması kopuk.");

        Assert.True(Contains(pixels, Palette.Ready.Color),
            "Hazır objektifin yeşili ekrana hiç çizilmedi.");

        Assert.True(Contains(pixels, Palette.For(ObjectiveKind.Dragon).Color),
            "Sakin satır kendi tür rengini kaybetmiş.");

        Assert.True(new FileInfo(path).Length > 0);
    }

    /// <summary>
    /// Looks for an exact colour among the rendered pixels. Text is antialiased at the edges, but
    /// the interior of a glyph is the unblended colour, so an exact match is the right test: a
    /// tolerant one would happily accept a neighbouring shade and miss a swapped brush.
    /// </summary>
    private static bool Contains(byte[] pixels, Color color)
    {
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            // Pbgra32, and the render is fully opaque over a black ground.
            if (pixels[i] == color.B && pixels[i + 1] == color.G && pixels[i + 2] == color.R)
            {
                return true;
            }
        }

        return false;
    }

    private static ObjectiveItem Objective(
        ObjectiveKind kind, string name, string remaining, bool imminent = false, bool ready = false) =>
        new(kind, name)
        {
            Remaining = remaining,
            IsImminent = imminent,
            IsReady = ready,
            Detail = ready ? "pitte" : string.Empty,
        };

    /// <summary>
    /// Pulls the objective ItemsControl straight out of the shipping XAML, with the shared theme
    /// and the window's own resources, so the bindings under test are the real ones.
    /// </summary>
    private static FrameworkElement LoadObjectiveRow()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "RiftOverlay", "OverlayWindow.xaml"));
        var theme = File.ReadAllText(Path.Combine(root, "src", "RiftOverlay", "Theme.xaml"));

        var markup = Element(source, "<ItemsControl Tag=\"objectives\"", "<ItemsControl", "</ItemsControl>");

        // The row's visibility is a MultiBinding on view-model flags that are false in a bare view
        // model, so the whole property element is dropped rather than overridden — setting the
        // attribute as well would collide with the element and fail to parse.
        var visibilityStart = markup.IndexOf("<ItemsControl.Visibility>", StringComparison.Ordinal);

        if (visibilityStart >= 0)
        {
            const string closing = "</ItemsControl.Visibility>";
            var visibilityEnd = markup.IndexOf(closing, StringComparison.Ordinal) + closing.Length;

            markup = markup.Remove(visibilityStart, visibilityEnd - visibilityStart);
        }

        var document = $"""
            <Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:vm="clr-namespace:RiftOverlay.ViewModels;assembly=RiftOverlay"
                    x:Name="PanelHost">
              <Canvas.Resources>
                {Slice(theme, ">", "</ResourceDictionary>")}
                {Slice(source, "<Window.Resources>", "</Window.Resources>")}
              </Canvas.Resources>
              {markup}
            </Canvas>
            """;

        return (FrameworkElement)XamlReader.Parse(document);
    }

    private static string Slice(string source, string open, string close)
    {
        var start = source.IndexOf(open, StringComparison.Ordinal);
        var end = source.IndexOf(close, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, $"{open} bulunamadı.");

        return source[(start + open.Length)..end];
    }

    /// <summary>
    /// Takes one element by counting its own tags. Property-element tags such as
    /// <c>&lt;ItemsControl.ItemTemplate&gt;</c> share the opening name, so a tag only counts when
    /// the character after the name is whitespace or the end of the tag — never a dot.
    /// </summary>
    private static string Element(string source, string opening, string open, string close)
    {
        var start = source.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening} bulunamadı.");

        var depth = 0;
        var index = start;

        while (index < source.Length)
        {
            var nextOpen = NextOpeningTag(source, index, open);
            var nextClose = source.IndexOf(close, index, StringComparison.Ordinal);

            Assert.True(nextClose >= 0, "Kapanmayan etiket.");

            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                depth++;
                index = nextOpen + open.Length;
                continue;
            }

            depth--;
            index = nextClose + close.Length;

            if (depth == 0)
            {
                return source[start..index];
            }
        }

        throw new InvalidOperationException("Etiket sınırları bulunamadı.");
    }

    private static int NextOpeningTag(string source, int from, string open)
    {
        var index = from;

        while ((index = source.IndexOf(open, index, StringComparison.Ordinal)) >= 0)
        {
            var after = index + open.Length;

            if (after < source.Length && (char.IsWhiteSpace(source[after]) || source[after] == '>'))
            {
                return index;
            }

            index = after;
        }

        return -1;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RiftOverlay.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static T OnStaThread<T>(Func<T> action)
    {
        T? result = default;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("Objektif satırı render edilemedi.", failure);
        }

        return result!;
    }
}
