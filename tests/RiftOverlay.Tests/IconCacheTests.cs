using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media.Imaging;
using RiftOverlay.Services;

namespace RiftOverlay.Tests;

/// <summary>
/// Covers the icon cache without touching the network. The behaviour that matters here is what
/// happens when things go wrong — a missing icon, a dead CDN, a patch change — because the overlay
/// draws these rows four times a second and must never stall or throw on artwork.
/// </summary>
public sealed class IconCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"riftoverlay-icons-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task AnIconIsDownloadedDecodedAndKept()
    {
        var handler = new CountingHandler(Png());
        var cache = new IconCache(_directory, handler: handler);

        cache.UseVersion("16.17.1");

        Assert.NotNull(await cache.GetItemIconAsync(6655));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task TheSameIconIsNotFetchedTwice()
    {
        var handler = new CountingHandler(Png());
        var cache = new IconCache(_directory, handler: handler);

        cache.UseVersion("16.17.1");

        await cache.GetItemIconAsync(6655);
        await cache.GetItemIconAsync(6655);

        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task ASecondRunReadsFromDiskWithNoNetwork()
    {
        var first = new CountingHandler(Png());
        var cache = new IconCache(_directory, handler: first);
        cache.UseVersion("16.17.1");
        await cache.GetItemIconAsync(6655);

        // A fresh process with the CDN unreachable must still show yesterday's artwork.
        var offline = new CountingHandler(null, HttpStatusCode.ServiceUnavailable);
        var second = new IconCache(_directory, handler: offline);
        second.UseVersion("16.17.1");

        Assert.NotNull(await second.GetItemIconAsync(6655));
        Assert.Equal(0, offline.Requests);
    }

    [Fact]
    public async Task AMissingIconIsRememberedSoItIsNotAskedForAgain()
    {
        // Data Dragon answers 403 rather than 404 for an id it does not have, and the overlay polls
        // constantly — without remembering the miss this would be a request every quarter second
        // for the rest of the match.
        var handler = new CountingHandler(null, HttpStatusCode.Forbidden);
        var cache = new IconCache(_directory, handler: handler);

        cache.UseVersion("16.17.1");

        Assert.Null(await cache.GetItemIconAsync(9999));
        Assert.Null(await cache.GetItemIconAsync(9999));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task ANetworkFailureReturnsNullInsteadOfThrowing()
    {
        var handler = new ThrowingHandler();
        var cache = new IconCache(_directory, handler: handler);

        cache.UseVersion("16.17.1");

        // The caller is a fire-and-forget path on the UI thread; an exception here would take the
        // process down over a picture.
        Assert.Null(await cache.GetItemIconAsync(6655));
    }

    [Fact]
    public async Task NothingIsFetchedBeforeAPatchIsKnown()
    {
        var handler = new CountingHandler(Png());
        var cache = new IconCache(_directory, handler: handler);

        Assert.False(cache.IsReady);
        Assert.Null(await cache.GetItemIconAsync(6655));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task ChangingPatchRefetches()
    {
        // The same id can mean a different item after a patch, so held artwork has to be dropped.
        var handler = new CountingHandler(Png());
        var cache = new IconCache(_directory, handler: handler);

        cache.UseVersion("16.16.1");
        await cache.GetItemIconAsync(6655);

        cache.UseVersion("16.17.1");
        await cache.GetItemIconAsync(6655);

        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task AnInvalidIdIsRejectedWithoutARequest()
    {
        var handler = new CountingHandler(Png());
        var cache = new IconCache(_directory, handler: handler);

        cache.UseVersion("16.17.1");

        Assert.Null(await cache.GetItemIconAsync(0));
        Assert.Equal(0, handler.Requests);
    }

    /// <summary>A real one-pixel PNG, so the decoder is genuinely exercised.</summary>
    private static byte[] Png()
    {
        var bitmap = new WriteableBitmap(1, 1, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null);
        var encoder = new PngBitmapEncoder();

        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }

    private sealed class CountingHandler(byte[]? payload, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;

            var response = new HttpResponseMessage(status);

            if (payload is not null)
            {
                response.Content = new ByteArrayContent(payload);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("ağ yok");
    }
}
