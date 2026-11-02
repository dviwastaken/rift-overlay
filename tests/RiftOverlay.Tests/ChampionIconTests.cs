using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RiftOverlay.Services;

namespace RiftOverlay.Tests;

/// <summary>
/// Champion portraits, which reach the cache by a different route from item artwork: an id has to
/// be turned into the name Data Dragon files the picture under, and that table is a download of its
/// own. Nothing here touches the network.
/// </summary>
public sealed class ChampionIconTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"riftoverlay-champ-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task APortraitIsFetchedThroughTheKeyTableRatherThanTheName()
    {
        // Wukong's artwork is filed as MonkeyKing. Building the url from the displayed name would
        // ask for Wukong.png and get nothing, so the id has to go through the table.
        var handler = new ScriptedHandler();
        var cache = new IconCache(_directory, handler: handler);

        cache.UseVersion("16.17.1");

        Assert.NotNull(await Settle(cache, 62));
        Assert.Contains(handler.Urls, url => url.EndsWith("champion/MonkeyKing.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ManyPortraitsAtOnceAllArriveAndTheTableIsFetchedOnce()
    {
        // The case that broke first, and it only broke on a cold cache: a draft asks for ten
        // portraits in the same instant, they all raced to fetch and write the key table, and the
        // losers threw on the half-written file. The failure was then remembered as "this champion
        // has no artwork", so the draft rendered blank squares that no retry ever filled.
        var handler = new ScriptedHandler();
        var cache = new IconCache(_directory, handler: handler);

        cache.UseVersion("16.17.1");

        int[] draft = [61, 64, 523, 50, 62, 145, 20, 36, 31, 888];

        foreach (var id in draft)
        {
            cache.TryGetChampionIcon(id, out _);
        }

        foreach (var id in draft)
        {
            Assert.NotNull(await Settle(cache, id));
        }

        Assert.Equal(1, handler.TableRequests);
    }

    [Fact]
    public async Task AFailedFetchIsRetriedRatherThanRememberedAsMissing()
    {
        // A thrown attempt is not an answer. Recording it as one is what turned a transient clash
        // into a portrait that stayed blank for the rest of the session.
        var handler = new ScriptedHandler { Fail = true };
        var cache = new IconCache(_directory, handler: handler);

        cache.UseVersion("16.17.1");

        Assert.Null(await Settle(cache, 61, expectImage: false, seconds: 0.5));

        handler.Fail = false;

        Assert.NotNull(await Settle(cache, 61));
    }

    [Fact]
    public async Task AChampionWithNoEntryInTheTableIsNotAskedForAgain()
    {
        // This one *is* an answer: the table loaded and the id is not in it. Remembering it stops a
        // request every second for a champion that will never resolve.
        var handler = new ScriptedHandler();
        var cache = new IconCache(_directory, handler: handler);

        cache.UseVersion("16.17.1");

        Assert.Null(await Settle(cache, 99999, expectImage: false));

        var after = handler.Urls.Count;

        cache.TryGetChampionIcon(99999, out var icon);

        Assert.Null(icon);
        Assert.Equal(after, handler.Urls.Count);
    }

    [Fact]
    public async Task ASecondRunReadsBothTheTableAndThePortraitFromDisk()
    {
        var first = new ScriptedHandler();
        var cache = new IconCache(_directory, handler: first);
        cache.UseVersion("16.17.1");
        await Settle(cache, 61);

        var second = new ScriptedHandler();
        var reopened = new IconCache(_directory, handler: second);
        reopened.UseVersion("16.17.1");

        Assert.NotNull(await Settle(reopened, 61));
        Assert.Empty(second.Urls);
    }

    /// <summary>
    /// Asks until the answer settles. The cache hands back only what it already has and starts the
    /// fetch behind it, which is what keeps champion select's once-a-second row comparison working;
    /// a test has no poll behind it, so it does the asking itself.
    /// </summary>
    private static async Task<ImageSource?> Settle(
        IconCache cache, int championId, bool expectImage = true, double seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < deadline)
        {
            if (cache.TryGetChampionIcon(championId, out var icon))
            {
                return icon;
            }

            await Task.Delay(10);
        }

        if (expectImage)
        {
            Assert.Fail($"Portre yerleşmedi: {championId}");
        }

        return null;
    }

    /// <summary>
    /// Answers the key table with a handful of real id/name pairs and every image request with a
    /// one-pixel png. The pairs are the awkward ones: each of these is a champion whose artwork
    /// name cannot be derived from what the client displays.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private const string Table = """
            {"data":{
              "Orianna":{"key":"61","id":"Orianna"},
              "LeeSin":{"key":"64","id":"LeeSin"},
              "Aphelios":{"key":"523","id":"Aphelios"},
              "Swain":{"key":"50","id":"Swain"},
              "MonkeyKing":{"key":"62","id":"MonkeyKing"},
              "Kaisa":{"key":"145","id":"Kaisa"},
              "Nunu":{"key":"20","id":"Nunu"},
              "DrMundo":{"key":"36","id":"DrMundo"},
              "Chogath":{"key":"31","id":"Chogath"},
              "Renata":{"key":"888","id":"Renata"}
            }}
            """;

        private readonly Lock _lock = new();

        public List<string> Urls { get; } = [];

        public int TableRequests { get; private set; }

        public bool Fail { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var isTable = url.EndsWith("champion.json", StringComparison.Ordinal);

            lock (_lock)
            {
                Urls.Add(url);

                if (isTable)
                {
                    TableRequests++;
                }
            }

            if (Fail)
            {
                throw new HttpRequestException("ağ yok");
            }

            // A real fetch does not return on the calling thread. Yielding here is what lets the
            // concurrency test actually overlap rather than running one request at a time.
            await Task.Delay(5, cancellationToken);

            return isTable
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Table) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png()) };
        }

        private static byte[] Png()
        {
            var bitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
            var encoder = new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = new MemoryStream();
            encoder.Save(stream);

            return stream.ToArray();
        }
    }
}
