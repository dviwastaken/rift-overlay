using System.Text.Json;
using RiftOverlay.Services;
using RiftOverlay.Services.Lcu;

namespace LiveApiProbe;

/// <summary>
/// Performs one real item-set write and proves it destroyed nothing.
/// <para>
/// This is the only tool here that changes anything, and it exists because the write endpoint
/// replaces the account's entire set collection in a single request. Reading round-trips cleanly is
/// necessary but not sufficient — the client could still reject or reshape what it is sent. So the
/// write is bracketed: every existing set is recorded by title and uid first, the write runs, and
/// then each one is checked to still be there. A survivor list that comes up short is reported as a
/// failure rather than a success with a footnote.
/// </para>
/// </summary>
internal static class ItemSetWriteProbe
{
    public static async Task<int> RunAsync(string championName, int championId)
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

        var before = await client.GetAsync<ItemSetCollection>(path);

        if (before is null)
        {
            Console.WriteLine("Mevcut setler okunamadı; yazma yapılmadı.");
            return 1;
        }

        // Recorded by uid, not by title: titles can repeat, and a uid is what actually identifies
        // a set the user would miss. Our own sets are excluded, because replacing one is the whole
        // point of the write — including them made a correct second run report a lost set.
        var existing = before.ItemSets
            .Where(s => !s.Title.StartsWith(ItemSetService.ManagedPrefix, StringComparison.Ordinal))
            .ToDictionary(s => s.Uid, s => s.Title);

        Console.WriteLine($"ÖNCE : {before.ItemSets.Count} set");
        Console.WriteLine($"       bizim setler   : {before.ItemSets.Count - existing.Count} (değişmesi beklenen)");
        Console.WriteLine($"       korunacaklar   : {existing.Count}");
        Console.WriteLine();

        var service = new ItemSetService(client, log);

        // A recognisable, small set — enough to prove the shape is accepted.
        var set = ItemSetService.Build([1056, 2003, 2003], [3020], [6655, 4645, 3157]);

        Console.WriteLine($"YAZILIYOR: {championName} (id {championId})");
        var written = await service.ApplyAsync(championId, championName, set);
        Console.WriteLine();

        var after = await client.GetAsync<ItemSetCollection>(path);

        if (after is null)
        {
            Console.WriteLine("SONRA okunamadı — durum belirsiz, istemcide kontrol et.");
            return 1;
        }

        Console.WriteLine($"SONRA: {after.ItemSets.Count} set");

        var survivingUids = after.ItemSets.Select(s => s.Uid).ToHashSet(StringComparer.Ordinal);
        var lost = existing.Where(pair => !survivingUids.Contains(pair.Key)).ToList();

        if (lost.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"!!! BİZE AİT OLMAYAN {lost.Count} SET KAYBOLDU:");

            foreach (var (uid, title) in lost)
            {
                Console.WriteLine($"    {title}  ({uid})");
            }

            return 1;
        }

        Console.WriteLine($"Bize ait olmayan {existing.Count} setin hepsi duruyor.");

        var ours = after.ItemSets.FirstOrDefault(s =>
            s.Title.StartsWith(ItemSetService.ManagedPrefix, StringComparison.Ordinal));

        if (!written || ours is null)
        {
            Console.WriteLine("Ama bizim set yazılmadı.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Yazılan set: {ours.Title}");
        Console.WriteLine($"  şampiyon : {string.Join(", ", ours.AssociatedChampions)}");
        Console.WriteLine($"  harita   : {string.Join(", ", ours.AssociatedMaps)}");

        foreach (var block in ours.Blocks)
        {
            var items = block.Items.Select(i => $"{i.Id}×{i.Count}");
            Console.WriteLine($"  {block.Type,-12}: {string.Join(", ", items)}");
        }

        Console.WriteLine();
        Console.WriteLine($"Sonuç: {before.ItemSets.Count} → {after.ItemSets.Count} set, kayıp yok.");

        return 0;
    }


    /// <summary>
    /// Removes sets that are empty shells — no title and no blocks. These are the wreckage of a
    /// write that went out with the wrong property casing: the client kept the champion association
    /// and dropped everything else, leaving entries that cannot be selected or edited in game.
    /// <para>
    /// The condition is deliberately narrow. A set with a title, or with any block, is left alone
    /// whatever else is true of it — this only clears things that are already unusable.
    /// </para>
    /// </summary>
    public static async Task<int> CleanAsync()
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
        var collection = await client.GetAsync<ItemSetCollection>(path);

        if (collection is null)
        {
            Console.WriteLine("Setler okunamadı; hiçbir şey yapılmadı.");
            return 1;
        }

        var broken = collection.ItemSets
            .Where(s => string.IsNullOrWhiteSpace(s.Title) && s.Blocks.Count == 0)
            .ToList();

        Console.WriteLine($"Toplam {collection.ItemSets.Count} set, bozuk görünen {broken.Count} tane:");

        foreach (var set in broken)
        {
            Console.WriteLine($"  [{set.Uid[..8]}] başlıksız · şampiyon={string.Join(",", set.AssociatedChampions)} · blok=0");
        }

        if (broken.Count == 0)
        {
            Console.WriteLine("Temizlenecek bir şey yok.");
            return 0;
        }

        var keep = collection.ItemSets.Except(broken).ToList();

        Console.WriteLine();
        Console.WriteLine($"Korunacak {keep.Count} set:");

        foreach (var set in keep)
        {
            Console.WriteLine($"  {set.Title} ({set.Blocks.Count} blok)");
        }

        collection.ItemSets = keep;

        var written = await client.PutAsync(path, collection);

        var after = await client.GetAsync<ItemSetCollection>(path);

        Console.WriteLine();
        Console.WriteLine(written && after is not null
            ? $"Temizlendi: {after.ItemSets.Count} set kaldı."
            : "Temizlenemedi.");

        return written ? 0 : 1;
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
