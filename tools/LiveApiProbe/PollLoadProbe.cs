using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using RiftOverlay.Models;
using RiftOverlay.Services;

namespace LiveApiProbe;

/// <summary>
/// Measures what the overlay's polling actually costs, so the decision to slow it down is made on
/// numbers rather than on the assumption that four requests a second must be expensive.
/// <para>
/// It also answers a question the models still guess at: <c>GameEnd</c> carries its result in a
/// field whose name has never been confirmed. The three values are known from the game binary, but
/// no capture of a finished game exists — so this dumps that event verbatim the moment it appears.
/// </para>
/// <para>
/// Read-only, on loopback, at the same rate the overlay already uses. It adds one more reader to an
/// endpoint the overlay is polling anyway.
/// </para>
/// </summary>
internal static class PollLoadProbe
{
    private const string Base = "https://127.0.0.1:2999/liveclientdata";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    public static async Task<int> RunAsync(int seconds)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };

        Console.WriteLine("Maç bekleniyor…");

        while (await ReadAsync(client, "allgamedata") is null)
        {
            await Task.Delay(2000);
        }

        Console.WriteLine($"Maç bulundu. {seconds} saniye ölçülüyor (overlay'in kendi hızıyla, 250 ms).");
        Console.WriteLine();

        var samples = new List<Sample>();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var endEvent = (string?)null;

        while (DateTime.UtcNow < deadline)
        {
            var sample = await MeasureAsync(client);

            if (sample is not null)
            {
                samples.Add(sample);
                endEvent ??= FindGameEnd(sample.Body);
            }

            await Task.Delay(250);
        }

        if (samples.Count == 0)
        {
            Console.WriteLine("Hiç örnek alınamadı — maç bitmiş olabilir.");
            return 1;
        }

        Report(samples, seconds);

        // Compare the split Riot recommends against the single call actually used.
        await CompareEndpointsAsync(client);

        Console.WriteLine();
        Console.WriteLine("Ölçüm bitti. Maçın sonuna kadar GameEnd bekleniyor (2 saniyede bir).");
        Console.WriteLine("Bu faz hiçbir şey ölçmüyor; yalnızca hâlâ adı doğrulanmamış tek alanı yakalamak için.");

        endEvent ??= await WaitForGameEndAsync(client);

        Console.WriteLine();

        if (endEvent is null)
        {
            Console.WriteLine("GameEnd görülmedi — maç bu araç kapanmadan bitmemiş olabilir.");
            return 0;
        }

        Console.WriteLine("=== GameEnd yakalandı — ham olay ===");
        Console.WriteLine(endEvent);
        Console.WriteLine();
        Console.WriteLine("Sonucu taşıyan alan yukarıdaki anahtarlardan biri: \"Result\" mı, \"GameEnd\" mi?");

        return 0;
    }

    /// <summary>
    /// Polls slowly until the game ends or the connection goes away.
    /// <para>
    /// Two seconds rather than four times a second: nothing here is being timed, and a probe that
    /// keeps hammering the endpoint for forty minutes to catch one event would be its own answer to
    /// the question this tool is meant to settle.
    /// </para>
    /// </summary>
    private static async Task<string?> WaitForGameEndAsync(HttpClient client)
    {
        var deadline = DateTime.UtcNow.AddMinutes(60);
        var missed = 0;

        while (DateTime.UtcNow < deadline)
        {
            var body = await ReadAsync(client, "allgamedata");

            if (body is null)
            {
                // The endpoint disappears the moment the game closes, which can happen before the
                // final event is ever polled. Several misses in a row means the game is gone.
                if (++missed >= 5)
                {
                    return null;
                }
            }
            else
            {
                missed = 0;

                var found = FindGameEnd(body);

                if (found is not null)
                {
                    return found;
                }
            }

            await Task.Delay(2000);
        }

        return null;
    }

    private static void Report(List<Sample> samples, int seconds)
    {
        var bytes = samples.Select(s => (double)s.Bytes).ToList();
        var parse = samples.Select(s => s.ParseMs).ToList();

        Console.WriteLine($"Örnek sayısı        : {samples.Count} ({seconds} saniyede)");
        Console.WriteLine($"allgamedata boyutu  : ort {bytes.Average() / 1024:F1} KB · en büyük {bytes.Max() / 1024:F1} KB");
        Console.WriteLine($"Çözümleme süresi    : ort {parse.Average():F2} ms · en yavaş {parse.Max():F2} ms");
        Console.WriteLine();

        var perSecond = bytes.Average() * 4;
        var parsePerSecond = parse.Average() * 4;

        Console.WriteLine($"Saniyede (250 ms)   : {perSecond / 1024:F0} KB okuma · {parsePerSecond:F1} ms çözümleme");
        Console.WriteLine($"30 dakikalık maçta  : {perSecond * 1800 / 1024 / 1024:F0} MB · {parsePerSecond * 1800 / 1000:F0} saniye CPU");
        Console.WriteLine();
        Console.WriteLine($"500 ms olsaydı      : {perSecond / 2 / 1024:F0} KB/sn · {parsePerSecond / 2:F1} ms/sn çözümleme");
        Console.WriteLine($"1 sn olsaydı        : {perSecond / 4 / 1024:F0} KB/sn · {parsePerSecond / 4:F1} ms/sn çözümleme");
    }

    /// <summary>
    /// Sizes the endpoints separately, because the standing advice is to split
    /// <c>allgamedata</c> into <c>eventdata</c> and <c>playerlist</c> — advice that only helps if
    /// the parts are actually smaller than the whole, and only applies if nothing needs the third
    /// part. The overlay does need it: the build panel reads the active player's gold.
    /// </summary>
    private static async Task CompareEndpointsAsync(HttpClient client)
    {
        Console.WriteLine();
        Console.WriteLine("=== Uçların boyutları ===");

        foreach (var path in new[] { "allgamedata", "eventdata", "playerlist", "activeplayer" })
        {
            var body = await ReadAsync(client, path);

            Console.WriteLine(body is null
                ? $"  {path,-14} okunamadı"
                : $"  {path,-14} {body.Length / 1024.0:F1} KB");
        }
    }

    private static async Task<Sample?> MeasureAsync(HttpClient client)
    {
        var body = await ReadAsync(client, "allgamedata");

        if (body is null)
        {
            return null;
        }

        var watch = Stopwatch.StartNew();

        try
        {
            JsonSerializer.Deserialize<GameSnapshot>(body, Options);
        }
        catch (Exception)
        {
            return null;
        }

        watch.Stop();

        return new Sample(body.Length, watch.Elapsed.TotalMilliseconds, body);
    }

    /// <summary>Returns the raw GameEnd event, so its field names can be read rather than assumed.</summary>
    private static string? FindGameEnd(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("events", out var events) ||
                !events.TryGetProperty("Events", out var list))
            {
                return null;
            }

            foreach (var item in list.EnumerateArray())
            {
                if (item.TryGetProperty("EventName", out var name) && name.GetString() == "GameEnd")
                {
                    return item.GetRawText();
                }
            }
        }
        catch (Exception)
        {
            // A malformed payload is not what this probe is measuring.
        }

        return null;
    }

    private static async Task<string?> ReadAsync(HttpClient client, string path)
    {
        try
        {
            return await client.GetStringAsync($"{Base}/{path}");
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record Sample(int Bytes, double ParseMs, string Body);
}
