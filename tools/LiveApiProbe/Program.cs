using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using RiftOverlay.Models;
using RiftOverlay.Services;

namespace LiveApiProbe;

/// <summary>
/// Samples the real Live Client Data API and reports how well it matches the models the overlay
/// binds to. Run it once per patch, or whenever the overlay shows something implausible: it names
/// the exact fields that drifted instead of leaving a silent null.
/// </summary>
internal static class Program
{
    private const string Endpoint = "https://127.0.0.1:2999/liveclientdata/allgamedata";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Event names <see cref="ObjectiveTracker"/> and the feed formatter know about.</summary>
    private static readonly HashSet<string> HandledEvents = new(StringComparer.Ordinal)
    {
        "GameStart", "GameEnd", "MinionsSpawning", "ChampionKill", "Multikill", "FirstBlood", "Ace",
        "DragonKill", "BaronKill", "HeraldKill", "HordeKill",
        "TurretKilled", "FirstBrick", "InhibKilled", "InhibRespawningSoon", "InhibRespawned",
    };

    private static async Task<int> Main(string[] args)
    {
        // İki ayrı doğrulama yolu aynı araçta: varsayılan mod oyunun canlı API'sini ölçüyor,
        // --opgg ise dış bağımlılığımız olan OP.GG sunucusunu. İkisi de "şema haber vermeden
        // değişebilir" sınıfından, o yüzden aynı yerde duruyorlar.
        if (args.Contains("--opgg", StringComparer.OrdinalIgnoreCase))
        {
            var champion = ArgValue(args, "--champion") ?? "Orianna";
            var position = ArgValue(args, "--position") ?? "mid";

            return await OpGgProbe.RunAsync(champion, position);
        }

        if (args.Contains("--delete-match", StringComparer.OrdinalIgnoreCase))
        {
            return int.TryParse(ArgValue(args, "--delete-match"), out var matchId)
                ? MatchDumpProbe.Delete(matchId)
                : 1;
        }

        if (args.Contains("--matches", StringComparer.OrdinalIgnoreCase))
        {
            return MatchDumpProbe.Run();
        }

        if (args.Contains("--poll-load", StringComparer.OrdinalIgnoreCase))
        {
            return await PollLoadProbe.RunAsync(ParseSeconds(args));
        }

        if (args.Contains("--lookup", StringComparer.OrdinalIgnoreCase))
        {
            return await LookupProbe.RunAsync(ArgValue(args, "--lookup") ?? string.Empty);
        }

        if (args.Contains("--clean-itemsets", StringComparer.OrdinalIgnoreCase))
        {
            return await ItemSetWriteProbe.CleanAsync();
        }

        // Bu araçtaki tek yazan mod; adı da bunu söylüyor.
        if (args.Contains("--write-itemset", StringComparer.OrdinalIgnoreCase))
        {
            return await ItemSetWriteProbe.RunAsync(
                ArgValue(args, "--champion") ?? "Orianna",
                int.TryParse(ArgValue(args, "--champion-id"), out var id) ? id : 61);
        }

        if (args.Contains("--itemsets", StringComparer.OrdinalIgnoreCase))
        {
            return await ItemSetProbe.RunAsync();
        }

        var seconds = ParseSeconds(args);

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };

        Console.WriteLine("Maç bekleniyor… (Pratik Aracı ya da özel maç başlat, Ctrl+C ile çık)");

        var firstJson = await WaitForGameAsync(client);
        Console.WriteLine($"Bağlandı. {seconds} saniye örnekleniyor…\n");

        var eventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var seenEventIds = new HashSet<int>();
        var deadSeen = new HashSet<string>(StringComparer.Ordinal);
        var lastJson = firstJson;
        double firstGameTime = -1, lastGameTime = -1;
        var samples = 0;
        var failures = 0;

        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var lastProgress = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            var json = await TryGetAsync(client);

            if (json is null)
            {
                failures++;
            }
            else
            {
                lastJson = json;
                samples++;

                var snapshot = JsonSerializer.Deserialize<GameSnapshot>(json, SerializerOptions);
                if (snapshot is not null)
                {
                    if (firstGameTime < 0)
                    {
                        firstGameTime = snapshot.GameData.GameTime;
                    }

                    lastGameTime = snapshot.GameData.GameTime;

                    // The feed is cumulative, so count each event once by id rather than per sample.
                    foreach (var gameEvent in snapshot.Events.Events.Where(e => seenEventIds.Add(e.EventId)))
                    {
                        eventCounts[gameEvent.EventName] = eventCounts.GetValueOrDefault(gameEvent.EventName) + 1;
                    }

                    // By champion only: including the countdown value would add a new entry every
                    // sample and bury the report under hundreds of near-identical strings.
                    foreach (var player in snapshot.AllPlayers.Where(p => p.RespawnTimer > 0))
                    {
                        deadSeen.Add(player.ChampionName);
                    }

                    if (DateTime.UtcNow - lastProgress >= TimeSpan.FromSeconds(15))
                    {
                        lastProgress = DateTime.UtcNow;
                        var left = (int)(deadline - DateTime.UtcNow).TotalSeconds;
                        Console.WriteLine(
                            $"  {samples,4} örnek · oyun saati {TimeSpan.FromSeconds(lastGameTime):mm\\:ss} · {left}s kaldı");
                    }
                }
            }

            await Task.Delay(500);
        }

        var dumpPath = Path.Combine(Environment.CurrentDirectory, $"probe-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(dumpPath, Prettify(lastJson));

        Report(lastJson, eventCounts, deadSeen, samples, failures, firstGameTime, lastGameTime, dumpPath);
        return 0;
    }

    private static void Report(
        string json,
        Dictionary<string, int> eventCounts,
        HashSet<string> deadSeen,
        int samples,
        int failures,
        double firstGameTime,
        double lastGameTime,
        string dumpPath)
    {
        var snapshot = JsonSerializer.Deserialize<GameSnapshot>(json, SerializerOptions)!;

        Line("ÖRNEKLEME");
        Console.WriteLine($"  başarılı: {samples}   başarısız: {failures}");
        Console.WriteLine($"  oyun saati: {firstGameTime:F1}s → {lastGameTime:F1}s (ilerliyor mu: {(lastGameTime > firstGameTime ? "evet" : "HAYIR")})");
        Console.WriteLine($"  ham JSON: {dumpPath}");

        Line("OYUN");
        Console.WriteLine($"  mod: {snapshot.GameData.GameMode}   harita: {snapshot.GameData.MapName}");
        Console.WriteLine($"  aktif oyuncu: {Show(snapshot.ActivePlayer?.RiotIdGameName)} / {Show(snapshot.ActivePlayer?.SummonerName)}");

        Line($"OYUNCULAR ({snapshot.AllPlayers.Count})");
        foreach (var player in snapshot.AllPlayers)
        {
            Console.WriteLine(
                $"  {player.Team,-6} {player.ChampionName,-14} sv{player.Level,-3} " +
                $"{player.Scores.Kills}/{player.Scores.Deaths}/{player.Scores.Assists,-3} " +
                $"cs{player.Scores.CreepScore,-4} respawn={player.RespawnTimer:F1} " +
                $"pos={Show(player.Position),-8} bot={player.IsBot,-5} " +
                $"[{Show(player.RiotId)}]");
        }

        Line("ÖLÜM GÖRÜLEN");
        Console.WriteLine(deadSeen.Count == 0
            ? "  (yok — respawn sayacı doğrulanamadı, birini öldürt)"
            : "  " + string.Join(", ", deadSeen.Order()));

        Line("OLAYLAR");
        if (eventCounts.Count == 0)
        {
            Console.WriteLine("  (yok)");
        }

        foreach (var (name, count) in eventCounts.OrderByDescending(e => e.Value))
        {
            var known = HandledEvents.Contains(name) ? " " : " ← İŞLENMİYOR";
            Console.WriteLine($"  {name,-22} {count,4}{known}");
        }

        Line("ŞEMA FARKI");
        var unknownTop = UnknownKeys(json, null, typeof(GameSnapshot));
        var unknownPlayer = UnknownKeys(json, "allPlayers", typeof(PlayerState));

        Console.WriteLine($"  modellenmeyen üst seviye alanlar : {Join(unknownTop)}");
        Console.WriteLine($"  modellenmeyen oyuncu alanları    : {Join(unknownPlayer)}");

        var missing = MissingKeys(json, typeof(GameSnapshot));
        Console.WriteLine($"  modelde olup JSON'da OLMAYAN     : {Join(missing)}");

        if (missing.Count > 0)
        {
            Console.WriteLine("\n  ⚠ Son satır boş değilse şema kaymış demektir — Models/GameSnapshot.cs güncellenmeli.");
        }
    }

    /// <summary>
    /// Fields the API returns that no model property claims. Harmless on their own, but they are
    /// where new features (and renamed fields) show up first.
    /// </summary>
    private static List<string> UnknownKeys(string json, string? arrayProperty, Type modelType)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (arrayProperty is not null)
        {
            if (!root.TryGetProperty(arrayProperty, out var array) || array.GetArrayLength() == 0)
            {
                return [];
            }

            root = array[0];
        }

        var modelled = modelType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return root.EnumerateObject()
            .Where(p => !modelled.Contains(p.Name))
            .Select(p => p.Name)
            .ToList();
    }

    private static List<string> MissingKeys(string json, Type modelType)
    {
        using var document = JsonDocument.Parse(json);

        var present = document.RootElement
            .EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return modelType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !present.Contains(name))
            .ToList();
    }

    private static async Task<string> WaitForGameAsync(HttpClient client)
    {
        while (true)
        {
            var json = await TryGetAsync(client);

            if (json is not null && json.Contains("allPlayers", StringComparison.Ordinal))
            {
                return json;
            }

            await Task.Delay(1000);
        }
    }

    private static async Task<string?> TryGetAsync(HttpClient client)
    {
        try
        {
            return await client.GetStringAsync(Endpoint);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Reads "--flag value" style arguments; null when the flag is absent.</summary>
    private static string? ArgValue(string[] args, string flag)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int ParseSeconds(string[] args)
    {
        var index = Array.FindIndex(args, a => a is "--seconds" or "-s");

        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value)
            ? value
            : 30;
    }

    private static string Prettify(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "(boş)" : value;

    private static string Join(List<string> values) => values.Count == 0 ? "(yok)" : string.Join(", ", values);

    private static void Line(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"── {title} ".PadRight(76, '─'));
    }
}
