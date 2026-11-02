using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RiftOverlay.Client;
using RiftOverlay.Services;
using RiftOverlay.Services.Lcu;
using RiftOverlay.Services.OpGg;

namespace RiftOverlay.Tests;

/// <summary>
/// Renders the client window offscreen, the same way the overlay panels are checked: into a bitmap
/// in memory, never a screen capture. The window is measured and arranged but never shown, so this
/// runs with no game, no client and nothing appearing on the user's desktop.
/// <para>
/// The assertions catch the failure that actually happens — a brush or style that no longer
/// resolves after resources move between dictionaries, which collapses the layout. The PNG left in
/// the temp directory is for looking at.
/// </para>
/// </summary>
public sealed class ClientWindowRenderTests
{
    [Fact]
    public void TheClientWindow_ResolvesItsResourcesAndRenders()
    {
        var (width, height, path) = OnStaThread(() =>
        {
            EnsureApplicationResources();

            // Neither dependency needs anything running to be constructed: the LCU client simply
            // reports "not connected", and the build view model has not been asked for anything.
            // A rune service is supplied so the apply button is part of the render: it is hidden
            // when runes cannot be written, and a hidden control is exactly what a broken binding
            // looks like.
            // Real artwork from the disk cache so the icon strip is part of what gets rendered.
            var icons = new IconCache();
            icons.UseVersion("16.17.1");

            var build = new BuildViewModel(
                new OpGgClient(),
                new SummonerSpellCatalog(),
                new RunePageService(new LcuClient()),
                new ItemSetService(new LcuClient()),
                icons);

            // Filled from the captured OP.GG response rather than a live call: the render stays
            // offline and deterministic, and what gets drawn is real data that once came off the
            // wire — including the sample sizes, which are the part that must never go missing.
            var analysis = OpGgResponseParser.Parse(Fixture());

            build.Show(analysis);

            // Icons are fetched after the rows exist, and this thread runs no dispatcher loop to
            // resume those continuations on. Waiting for them is not test decoration: without it
            // the render would silently be of a build with no artwork, and a broken icon path
            // would look exactly the same as a slow one.
            WaitForIcons(build, ExpectedIconCount(analysis));

            // The player pane is filled from a hand-built profile so both sections render with
            // real content. Nothing here touches the network or the client.
            var player = new PlayerViewModel(
                new SummonerLookupService(new LcuClient(), new ChampionNames(new LcuClient())), icons);

            player.Show(new PlayerProfile("Oyuncu", "TR1", 536, "puuid",
                [
                    // Losses come back as zero from the live endpoint, so this row must show a win
                    // count and no percentage — the case most likely to regress.
                    new RankEntry("RANKED_SOLO_5x5", "SILVER", "I", 16, 163, 0),
                    new RankEntry("RANKED_FLEX_SR", "SILVER", "IV", 54, 11, 0),
                ],
                [
                    new MatchSummary(1, "Twitch", "CLASSIC", false, 23, 11, 2, 193, 17, TimeSpan.FromSeconds(1956), DateTime.Now, 29),
                    new MatchSummary(2, "Twitch", "CLASSIC", true, 20, 12, 7, 288, 18, TimeSpan.FromSeconds(2418), DateTime.Now, 29),
                    new MatchSummary(3, "Gnar", "KIWI", false, 12, 22, 19, 47, 18, TimeSpan.FromSeconds(1473), DateTime.Now, 150),
                ]));

            // Show() builds the rows without artwork on purpose; this is the step the search flow
            // runs straight after, and without it the render would be of a list of blank squares.
            player.LoadPortraitsAsync().GetAwaiter().GetResult();

            // Statistics are loaded from records passed straight in, so the render needs no
            // database file and no game history on this machine.
            var stats = new StatsViewModel(new MatchDatabase(
                Path.Combine(Path.GetTempPath(), $"riftoverlay-render-{Guid.NewGuid():N}.db")));

            // A champion select in progress, so the section renders with both teams rather than
            // the idle line it shows the rest of the time.
            // Real champion ids so the portraits are fetched the way they are in the client. The
            // ids are the only route to the artwork: a champion's displayed name cannot be turned
            // into its file name — Wukong is filed as MonkeyKing, Kai'Sa as Kaisa.
            var champSelect = new ChampSelectViewModel(icons: icons);

            var session = new ChampSelectState(
                "ChampSelect",
                [
                    new ChampSelectPick("Orianna", "MIDDLE", IsLocked: true, IsSelf: true, ChampionId: 61),
                    new ChampSelectPick("Lee Sin", "JUNGLE", IsLocked: true, IsSelf: false, ChampionId: 64),
                    new ChampSelectPick("Aphelios", "BOTTOM", IsLocked: false, IsSelf: false, ChampionId: 523),
                ],
                [
                    new ChampSelectPick("Swain", "MIDDLE", IsLocked: true, IsSelf: false, ChampionId: 50),
                    new ChampSelectPick("—", "", IsLocked: false, IsSelf: false),
                ],
                ["Yasuo", "Katarina"],
                27,
                "Orianna",
                SelfLocked: true);

            champSelect.OnState(session);

            var window = new ClientWindow(new LcuClient(), build, player, stats, champSelect);

            // Measuring the Window itself lays out nothing while it has never been shown, so the
            // content is measured directly. Its resource lookup still walks up through the Window,
            // which is what makes the window-scoped styles resolve here.
            var content = (FrameworkElement)window.Content;

            // Both panes are rendered, not just the default one: a pane that fails to bind is
            // invisible precisely because it is collapsed, which is the failure worth catching.
            window.ShowSection(ClientSection.Build);
            Layout(content);

            // The window paints its own Background behind the content, so the render has to as
            // well. Without it every glyph lands on transparent pixels and the antialiased edges
            // read as washed-out text in the PNG — a rendering artefact that looks exactly like a
            // real contrast bug and would send the next person hunting for one.
            var file = Render(window, content, "riftoverlay-client-build.png");

            // The build pane runs past the window height, so the sections below the fold are
            // rendered by scrolling to them rather than by laying the content out taller.
            // Enlarging it was the first attempt and it was quietly wrong: at a height the window
            // never has, the pane arranged 28 pixels wider than its viewport and the rate columns
            // fell off the right edge — a layout artefact indistinguishable in a PNG from a real
            // overflow bug. Scrolling renders exactly what a user sees.
            // Three views because the pane is three screens tall and each one carries something
            // the others do not: the headline strip at the top, the levelling grid in the middle,
            // and the slot options with the apply buttons at the end.
            ScrollTo(window, content, 330);
            Render(window, content, "riftoverlay-client-build-middle.png");

            ScrollToBottom(window, content);
            Render(window, content, "riftoverlay-client-build-scrolled.png");
            ResetScroll(window, content);

            var width = content.ActualWidth;
            var height = content.ActualHeight;

            window.ShowSection(ClientSection.Player);
            Layout(content);

            Render(window, content, "riftoverlay-client-player.png");

            // Opening the section refreshes from disk — that is what the app does, and it would
            // wipe anything injected beforehand. So the rows go in after the switch, not before.
            window.ShowSection(ClientSection.ChampSelect);

            // Four of the five seats have a champion; the fifth is an empty seat with no id.
            WaitForPortraits(champSelect, session, expected: 4);

            Layout(content);

            Render(window, content, "riftoverlay-client-champselect.png");

            window.ShowSection(ClientSection.Stats);

            stats.Load(
                [
                    Match("Orianna", "Win", 7, 2, 9, 1694),
                    Match("Ahri", "Lose", 3, 5, 6, 1862),
                    // A match whose result was never seen: counts as played, neither won nor lost.
                    Match("Lee Sin", null, 5, 5, 11, 1120),
                ],
                [
                    new ChampionStat("Orianna", 12, 12, 7, 6.2, 3.1, 8.4, 214, 21.5, 68.4),
                    new ChampionStat("Ahri", 5, 4, 1, 4.8, 5.2, 6.0, 198, 19.0, 61.0),
                    // No decided match: the win rate must read as unknown, not as 0%.
                    new ChampionStat("Lee Sin", 2, 0, 0, 5.0, 5.0, 11.0, 90, 28.0, null),
                ]);

            Layout(content);

            Render(window, content, "riftoverlay-client-stats.png");

            return (width, height, file);
        });

        Assert.True(width > 800, $"İçerik çok dar: {width}");
        Assert.True(height > 500, $"İçerik çok kısa: {height}");
        Assert.True(new FileInfo(path).Length > 0);
    }

    /// <summary>
    /// Blocks until the item rows have their artwork, or gives up. The timeout is generous because
    /// the first run downloads from the CDN; every later run reads the disk cache and returns at
    /// once.
    /// </summary>
    private static MatchRecord Match(
        string champion, string? result, int kills, int deaths, int assists, double duration) =>
        new(1, DateTime.UtcNow, DateTime.UtcNow, "CLASSIC", "Map11", champion, "MIDDLE", "ORDER",
            result, duration, kills, deaths, assists, 214, 22, 16);

    /// <summary>
    /// Lays the content out at the window's size.
    /// <para>
    /// The arrange comes last on purpose. <c>UpdateLayout</c> on an element that is not attached to
    /// a live window re-arranges it at its own desired size, so arranging first and updating after
    /// silently shrinks the result — the player pane, whose content is narrower, collapsed to 623
    /// pixels inside a 940-pixel bitmap that way.
    /// </para>
    /// </summary>
    private static void Layout(FrameworkElement content, int height = 640)
    {
        content.Measure(new Size(940, height));
        content.UpdateLayout();
        content.Arrange(new Rect(0, 0, 940, height));
    }

    /// <summary>
    /// Scrolls the content pane to its end and settles the layout, so the sections past the fold
    /// render at the window's real size instead of at an invented one.
    /// </summary>
    /// <summary>
    /// Re-applies the session until every portrait has arrived.
    /// <para>
    /// Portraits are part of a row's value, not a property set on it later, so a row only gains its
    /// picture when the state is applied again — which in the client is the next poll, a second
    /// away. Here there is no poll, so the wait does the applying. Without it the champion select
    /// render would silently be of a draft with no artwork, and a broken portrait path would look
    /// exactly like a slow one.
    /// </para>
    /// </summary>
    private static void WaitForPortraits(ChampSelectViewModel champSelect, ChampSelectState session, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        var total = 0;

        while (DateTime.UtcNow < deadline)
        {
            champSelect.OnState(session);

            total = champSelect.Allies.Count(line => line.Portrait is not null) +
                    champSelect.Enemies.Count(line => line.Portrait is not null);

            if (total >= expected)
            {
                return;
            }

            Thread.Sleep(50);
        }

        Assert.Fail($"Şampiyon portreleri gelmedi: {total}/{expected} — portre yolu kopuk olabilir.");
    }

    private static void ScrollTo(Window window, FrameworkElement content, double offset)
    {
        var scroll = (ScrollViewer)window.FindName("ContentScroll");

        scroll.ScrollToVerticalOffset(offset);
        scroll.UpdateLayout();
        Layout(content);
    }

    private static void ScrollToBottom(Window window, FrameworkElement content)
    {
        var scroll = (ScrollViewer)window.FindName("ContentScroll");

        scroll.ScrollToBottom();
        scroll.UpdateLayout();
        Layout(content);
    }

    private static void ResetScroll(Window window, FrameworkElement content)
    {
        var scroll = (ScrollViewer)window.FindName("ContentScroll");

        scroll.ScrollToTop();
        scroll.UpdateLayout();
        Layout(content);
    }

    /// <summary>
    /// Draws the window's background first and the content over it. Without the background every
    /// glyph lands on transparent pixels and its antialiased edges read as washed-out text — an
    /// artefact indistinguishable from a real contrast bug.
    /// </summary>
    private static string Render(Window window, FrameworkElement content, string name, int height = 640)
    {
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(window.Background, null, new Rect(0, 0, 940, height));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, 940, height));
        }

        var bitmap = new RenderTargetBitmap(940, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var file = Path.Combine(Path.GetTempPath(), name);

        using var stream = File.Create(file);
        encoder.Save(stream);

        return file;
    }

    /// <summary>How many icons the three item rows should end up with, taken from the fixture.</summary>
    private static int ExpectedIconCount(ChampionAnalysis analysis) =>
        analysis.StarterItems.Ids.Count + analysis.Boots.Ids.Count + analysis.CoreItems.Ids.Count +
        SlotIcons(analysis.FourthItems) + SlotIcons(analysis.FifthItems) + SlotIcons(analysis.SixthItems);

    /// <summary>Mirrors the view model's own cap of three options per slot.</summary>
    private static int SlotIcons(IReadOnlyList<BuildRow> rows) =>
        rows.Take(3).Sum(row => row.Ids.Count);

    /// <summary>
    /// Waits for an exact count rather than for the number to stop rising.
    /// <para>
    /// Settling was the first attempt and it was quietly wrong: under the parallel load of the full
    /// suite the gap between two downloads exceeded the settle window, so the wait declared itself
    /// finished at three icons and the test failed only when run alongside everything else. An
    /// expected total derived from the fixture has no such race — it either arrives or the deadline
    /// says the icon path is broken.
    /// </para>
    /// </summary>
    private static void WaitForIcons(BuildViewModel build, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        var total = 0;

        while (DateTime.UtcNow < deadline)
        {
            total = build.Lines.Sum(line => line.Icons.Count) +
                    build.Slots.Sum(slot => slot.Options.Sum(option => option.Icons.Count));

            if (total >= expected)
            {
                return;
            }

            Thread.Sleep(50);
        }

        Assert.Fail($"Eşya ikonları gelmedi: {total}/{expected} — ikon yolu kopuk olabilir.");
    }

    private static string Fixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RiftOverlay.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return File.ReadAllText(Path.Combine(
            directory!.FullName, "tests", "RiftOverlay.Tests", "Fixtures", "opgg-orianna-full.txt"));
    }

    /// <summary>
    /// The window's XAML resolves <c>PrimaryText</c> and friends through application resources,
    /// which a test host does not have. Creating the Application also registers the pack URI
    /// scheme that the theme dictionary is loaded through.
    /// </summary>
    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();

        var alreadyMerged = application.Resources.MergedDictionaries
            .Any(d => d.Source?.OriginalString.EndsWith("Theme.xaml", StringComparison.Ordinal) == true);

        if (!alreadyMerged)
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/RiftOverlay;component/Theme.xaml"),
            });
        }
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
            throw new InvalidOperationException("Client penceresi render edilemedi.", failure);
        }

        return result!;
    }
}
