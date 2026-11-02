using RiftOverlay.Services;
using RiftOverlay.Services.Lcu;

namespace LiveApiProbe;

/// <summary>
/// Looks a player up through the real service and prints what came back. Read-only.
/// <para>
/// The point is the wire shapes: rank tiers, the participant linking in match history, and the
/// champion names. A model that misses a field here does not throw — it returns a profile full of
/// defaults, which reads as "this player has no rank and no games" rather than as a bug.
/// </para>
/// </summary>
internal static class LookupProbe
{
    public static async Task<int> RunAsync(string riotId)
    {
        var log = new ConsoleLog();
        using var client = new LcuClient(log);

        if (!client.Refresh())
        {
            Console.WriteLine("İstemci kapalı.");
            return 1;
        }

        var service = new SummonerLookupService(client, new ChampionNames(client, log), log);

        Console.WriteLine($"Aranıyor: {riotId}");
        Console.WriteLine();

        var profile = await service.FindAsync(riotId);

        if (profile is null)
        {
            Console.WriteLine("Bulunamadı.");
            return 1;
        }

        Console.WriteLine($"{profile.RiotId} · seviye {profile.Level}");
        Console.WriteLine();

        if (profile.Ranks.Count == 0)
        {
            Console.WriteLine("Dereceli kaydı yok.");
        }
        else
        {
            foreach (var rank in profile.Ranks)
            {
                // No win rate unless losses were actually reported — the endpoint leaves that field
                // at zero, so computing one would print 100% for everybody.
                var record = rank.WinRate is { } r
                    ? $"{rank.Wins}G/{rank.Losses}M (%{r * 100:F0})"
                    : $"{rank.Wins} galibiyet (mağlubiyet bildirilmiyor)";

                Console.WriteLine($"  {rank.Queue,-18} {rank.Tier} {rank.Division} · {rank.LeaguePoints} LP · {record}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Son {profile.Matches.Count} maç:");

        foreach (var match in profile.Matches)
        {
            Console.WriteLine(
                $"  {(match.Won ? "G" : "M")} {match.Champion,-14} {match.Kills}/{match.Deaths}/{match.Assists,-3} " +
                $"CS {match.CreepScore,-4} sv{match.Level,-3} {match.Duration:mm\\:ss}  {match.Mode,-12} {match.PlayedAt:dd.MM HH:mm}");
        }

        // A profile that parsed but is entirely empty is the failure mode worth naming, because it
        // looks like a legitimate answer.
        if (profile.Matches.Count == 0 && profile.Ranks.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("UYARI: hem rank hem maç listesi boş — model alanları tutmuyor olabilir.");
            return 1;
        }

        return 0;
    }

    private sealed class ConsoleLog : ILog
    {
        public void Info(string message) => Console.WriteLine($"  [bilgi] {message}");

        public void Warn(string message) => Console.WriteLine($"  [uyarı] {message}");

        public void Error(string message, Exception? exception = null) =>
            Console.WriteLine($"  [hata] {message}");
    }
}
