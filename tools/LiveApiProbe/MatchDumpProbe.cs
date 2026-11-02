using RiftOverlay.Services;

namespace LiveApiProbe;

/// <summary>
/// Prints what the recorder has actually stored. Read-only.
/// <para>
/// Written to answer one question the tests cannot: whether a real match ends up in the database
/// with a result. <c>GameEnd</c> is the only source of win or loss, and if the live endpoint dies
/// with the game before that event is ever polled, every match would be filed with no result and
/// the win rate would stay permanently unknown.
/// </para>
/// </summary>
internal static class MatchDumpProbe
{
    /// <summary>
    /// Removes one match by id. Prints what it is about to remove first, because a match id means
    /// nothing on its own and deleting the wrong one is not recoverable.
    /// </summary>
    public static int Delete(int matchId)
    {
        var database = new MatchDatabase();
        var match = database.GetRecentMatches(100).FirstOrDefault(m => m.Id == matchId);

        if (match is null)
        {
            Console.WriteLine($"#{matchId} bulunamadı.");
            return 1;
        }

        Console.WriteLine($"Siliniyor: #{match.Id} {match.Champion} {match.Kills}/{match.Deaths}/{match.Assists} " +
            $"· {match.GameMode} · {match.StartedAtUtc.ToLocalTime():dd.MM HH:mm} · sonuç {match.Result ?? "yok"}");
        Console.WriteLine($"  {database.GetEvents(matchId).Count} olay, {database.GetSamples(matchId).Count} örnek de gidecek.");

        var deleted = database.DeleteMatch(matchId);

        Console.WriteLine(deleted ? "Silindi." : "Silinemedi.");
        Console.WriteLine();

        return Run();
    }

    public static int Run()
    {
        var database = new MatchDatabase();
        var matches = database.GetRecentMatches(10);

        if (matches.Count == 0)
        {
            Console.WriteLine("Kayıtlı maç yok.");
            return 0;
        }

        Console.WriteLine($"Son {matches.Count} maç:");
        Console.WriteLine();

        foreach (var match in matches)
        {
            var result = match.Result ?? "(sonuç yok)";
            var duration = TimeSpan.FromSeconds(match.DurationSeconds);

            Console.WriteLine(
                $"  #{match.Id,-3} {match.Champion,-14} {result,-12} " +
                $"{match.Kills}/{match.Deaths}/{match.Assists,-4} CS {match.CreepScore,-4} " +
                $"{(int)duration.TotalMinutes}:{duration.Seconds:00}  {match.GameMode,-10} " +
                $"{match.StartedAtUtc.ToLocalTime():dd.MM HH:mm}");
        }

        var withResult = matches.Count(m => m.Result is not null);

        Console.WriteLine();
        Console.WriteLine($"Sonucu kaydedilmiş: {withResult}/{matches.Count}");

        if (withResult == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Hiçbirinin sonucu yok — GameEnd olayı pratikte yakalanamıyor olabilir.");
        }

        var latest = matches[0];
        var events = database.GetEvents(latest.Id);
        var samples = database.GetSamples(latest.Id);

        Console.WriteLine();
        Console.WriteLine($"En son maç (#{latest.Id}): {events.Count} olay, {samples.Count} örnek");

        foreach (var name in events.Select(e => e.EventName).Distinct().Order())
        {
            Console.WriteLine($"    {name,-22} {events.Count(e => e.EventName == name)}");
        }

        return 0;
    }
}
