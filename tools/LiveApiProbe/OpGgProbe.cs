using RiftOverlay.Services;
using RiftOverlay.Services.OpGg;

namespace LiveApiProbe;

/// <summary>
/// Calls OP.GG's live server through the real <see cref="OpGgClient"/> and prints what came back.
/// <para>
/// It lives here rather than in the test suite on purpose: the unit tests parse a captured fixture
/// and must stay offline and deterministic, while this exercises the parts a fixture cannot — the
/// MCP handshake, the session header, the event-stream unwrapping and the live schema. OP.GG
/// publishes no versioning for this endpoint, so run it whenever the build panel looks wrong.
/// </para>
/// </summary>
internal static class OpGgProbe
{
    public static async Task<int> RunAsync(string champion, string position)
    {
        Console.WriteLine($"OP.GG sorgulanıyor: {champion} · {position}");
        Console.WriteLine();

        using var client = new OpGgClient(new ConsoleLog());

        var started = DateTime.UtcNow;
        var build = await client.GetBuildAsync(champion, position);
        var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;

        if (build is null)
        {
            Console.WriteLine($"BAŞARISIZ — yanıt alınamadı ({elapsed:F0} ms).");
            Console.WriteLine("Ağ kapalıysa beklenen sonuç bu; uygulamada panel sessizce gizlenir.");
            return 1;
        }

        Console.WriteLine($"Yanıt alındı ({elapsed:F0} ms)");
        Console.WriteLine();

        var stats = build.Summary.AverageStats;
        Console.WriteLine($"  Özet      : {stats.Play} maç · %{stats.WinRate * 100:F1} kazanma · KDA {stats.Kda:F2} · {build.DamageType}");

        Row("Başlangıç", build.StarterItems.Names, build.StarterItems.Play, build.StarterItems.Win);
        Row("Çizme", build.Boots.Names, build.Boots.Play, build.Boots.Win);
        Row("Çekirdek", build.CoreItems.Names, build.CoreItems.Play, build.CoreItems.Win);
        Row("Büyüler", build.SummonerSpells.Names, build.SummonerSpells.Play, build.SummonerSpells.Win);

        Console.WriteLine($"  Yetenek   : {string.Join(" ", build.Skills.Order)}  ({build.Skills.Play} maç)");
        Console.WriteLine($"  Maks sıra : {string.Join(" > ", build.SkillMasteries.Ids)}  ({build.SkillMasteries.Play} maç)");
        Console.WriteLine($"  Rünler    : {build.Runes.PrimaryPageName} [{string.Join(", ", build.Runes.PrimaryRuneNames)}]");
        Console.WriteLine($"              {build.Runes.SecondaryPageName} [{string.Join(", ", build.Runes.SecondaryRuneNames)}]  ({build.Runes.Play} maç)");

        if (build.WeakCounters.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Zorlandığı eşleşmeler:");

            foreach (var counter in build.WeakCounters)
            {
                Console.WriteLine($"    {counter.ChampionName,-14} %{counter.WinRate * 100:F1} rakip lehine  ({counter.Play} maç)");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Örneklem sayıları yukarıda bilerek her satırda: onlarsız bir öneri,");
        Console.WriteLine("6 maçlık bir tesadüfü otorite gibi gösterebilir.");

        return 0;
    }

    /// <summary>Never prints a build line without the sample size beside it.</summary>
    private static void Row(string label, List<string> names, int play, int win)
    {
        var rate = play == 0 ? "—" : $"%{100.0 * win / play:F1}";

        Console.WriteLine($"  {label,-10}: {string.Join(" → ", names),-52} {rate,6}  ({play} maç)");
    }

    private sealed class ConsoleLog : ILog
    {
        public void Info(string message) => Console.WriteLine($"  [bilgi] {message}");

        public void Warn(string message) => Console.WriteLine($"  [uyarı] {message}");

        public void Error(string message, Exception? exception = null) =>
            Console.WriteLine($"  [hata] {message}{(exception is null ? string.Empty : " — " + exception.Message)}");
    }
}
