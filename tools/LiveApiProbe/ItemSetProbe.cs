using System.Text.Json;
using RiftOverlay.Services;
using RiftOverlay.Services.Lcu;

namespace LiveApiProbe;

/// <summary>
/// Reads the account's item sets through the real model and checks nothing is lost on the way back
/// out. <b>It never writes.</b>
/// <para>
/// This exists because the write endpoint replaces the entire collection in one request. If the
/// model drops a field — a misspelled property name, a missing attribute — a later write would send
/// the collection back without it and quietly damage every set the user owns. Verifying the
/// round-trip separately, before any write path is enabled, is the only way to find that safely.
/// </para>
/// </summary>
internal static class ItemSetProbe
{
    // The write paths serializer, not a local copy. A private set of options here is exactly
    // what let a PascalCase write slip through while this probe reported the round-trip clean.
    private static JsonSerializerOptions Options => LcuJson.Options;

    public static async Task<int> RunAsync()
    {
        var log = new ConsoleLog();
        using var client = new LcuClient(log);

        if (!client.Refresh())
        {
            Console.WriteLine("İstemci kapalı.");
            return 1;
        }

        var summonerId = await ReadSummonerIdAsync(client);

        if (summonerId == 0)
        {
            Console.WriteLine("summonerId okunamadı.");
            return 1;
        }

        var path = $"/lol-item-sets/v1/item-sets/{summonerId}/sets";

        var raw = await client.GetAsync<JsonElement>(path);
        var typed = await client.GetAsync<ItemSetCollection>(path);

        if (typed is null || raw.ValueKind != JsonValueKind.Object)
        {
            Console.WriteLine("Eşya setleri okunamadı.");
            return 1;
        }

        Console.WriteLine($"Okundu: {typed.ItemSets.Count} set, accountId taşınıyor: {typed.AccountId != 0}");
        Console.WriteLine();

        var rawSets = raw.GetProperty("itemSets");

        if (rawSets.GetArrayLength() != typed.ItemSets.Count)
        {
            Console.WriteLine($"UYUŞMAZLIK: ham {rawSets.GetArrayLength()} set, modelde {typed.ItemSets.Count}");
            return 1;
        }

        // Compare field by field rather than trusting a count: a set can survive while one of its
        // properties is silently dropped.
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var set in rawSets.EnumerateArray())
        {
            var round = JsonSerializer.SerializeToElement(
                typed.ItemSets[rawSets.EnumerateArray().ToList().IndexOf(set)], Options);

            foreach (var property in set.EnumerateObject())
            {
                if (!round.TryGetProperty(property.Name, out _))
                {
                    missing.Add(property.Name);
                }
            }
        }

        if (missing.Count > 0)
        {
            Console.WriteLine("MODELDE OLMAYAN ALANLAR — yazma bunları silerdi:");

            foreach (var name in missing)
            {
                Console.WriteLine($"  {name}");
            }

            return 1;
        }

        Console.WriteLine("Gidiş-dönüş temiz: hiçbir alan kaybolmuyor.");
        Console.WriteLine($"Bizim yazdığımız setler: {typed.ItemSets.Count(s => s.Title.StartsWith(ItemSetService.ManagedPrefix, StringComparison.Ordinal))}");
        Console.WriteLine($"Başkasına ait setler   : {typed.ItemSets.Count(s => !s.Title.StartsWith(ItemSetService.ManagedPrefix, StringComparison.Ordinal))}");
        Console.WriteLine();
        Console.WriteLine("Bu araç hiçbir şey yazmadı.");

        return 0;
    }

    private static async Task<long> ReadSummonerIdAsync(LcuClient client)
    {
        var summoner = await client.GetAsync<JsonElement>("/lol-summoner/v1/current-summoner");

        return summoner.ValueKind == JsonValueKind.Object &&
               summoner.TryGetProperty("summonerId", out var id) &&
               id.TryGetInt64(out var value)
            ? value
            : 0;
    }

    private sealed class ConsoleLog : ILog
    {
        public void Info(string message) => Console.WriteLine($"  [bilgi] {message}");

        public void Warn(string message) => Console.WriteLine($"  [uyarı] {message}");

        public void Error(string message, Exception? exception = null) =>
            Console.WriteLine($"  [hata] {message}");
    }
}
