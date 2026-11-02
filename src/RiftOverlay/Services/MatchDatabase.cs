using System.IO;
using Microsoft.Data.Sqlite;

namespace RiftOverlay.Services;

/// <param name="Result">Win, Lose or Neither — null while the match is open or if it ended unseen.</param>
public sealed record MatchRecord(
    int Id,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    string GameMode,
    string MapName,
    string Champion,
    string Position,
    string Team,
    string? Result,
    double DurationSeconds,
    int Kills,
    int Deaths,
    int Assists,
    int CreepScore,
    double WardScore,
    int Level);

public sealed record MatchSample(
    double GameTime,
    int Kills,
    int Deaths,
    int Assists,
    int CreepScore,
    double WardScore,
    int Level,
    double Gold);

public sealed record MatchEventRow(int EventId, string EventName, double EventTime, string Detail);

/// <param name="Wins">Counts only matches whose result was actually seen, so it can trail <c>Matches</c>.</param>
/// <param name="AverageCreepScoreAtTen">Null when no match ran long enough to sample ten minutes.</param>
public sealed record ChampionStat(
    string Champion,
    int Matches,
    int DecidedMatches,
    int Wins,
    double AverageKills,
    double AverageDeaths,
    double AverageAssists,
    double AverageCreepScore,
    double AverageWardScore,
    double? AverageCreepScoreAtTen);

/// <summary>
/// Stores finished and in-progress matches in SQLite under <c>%LOCALAPPDATA%\RiftOverlay</c>.
/// <para>
/// Each call opens its own connection rather than holding one open: recording writes a handful of
/// rows a minute, so pooling buys nothing, and a short-lived connection cannot leave the file
/// locked if the overlay is killed mid-match.
/// </para>
/// </summary>
public sealed class MatchDatabase
{
    private readonly string _connectionString;
    private readonly ILog _log;

    /// <param name="databasePath">Overridden by tests; defaults to the per-user data directory.</param>
    public MatchDatabase(string? databasePath = null, ILog? log = null)
    {
        _log = log ?? NullLog.Instance;

        var path = databasePath ?? DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        _connectionString = $"Data Source={path}";
        EnsureCreated();
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RiftOverlay",
        "matches.db");

    private void EnsureCreated()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        // The unique index on (MatchId, EventId) is load-bearing, not tidiness. The live event feed
        // is cumulative — every poll returns the whole match's events again — so an insert that ran
        // twice would silently double-count kills. Paired with INSERT OR IGNORE it makes replaying
        // the feed harmless even if the recorder's own de-duplication is bypassed.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Matches (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StartedAtUtc TEXT NOT NULL,
                EndedAtUtc TEXT NULL,
                GameMode TEXT NOT NULL,
                MapName TEXT NOT NULL,
                Champion TEXT NOT NULL,
                Position TEXT NOT NULL,
                Team TEXT NOT NULL,
                Result TEXT NULL,
                DurationSeconds REAL NOT NULL DEFAULT 0,
                Kills INTEGER NOT NULL DEFAULT 0,
                Deaths INTEGER NOT NULL DEFAULT 0,
                Assists INTEGER NOT NULL DEFAULT 0,
                CreepScore INTEGER NOT NULL DEFAULT 0,
                WardScore REAL NOT NULL DEFAULT 0,
                Level INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS MatchEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MatchId INTEGER NOT NULL REFERENCES Matches(Id),
                EventId INTEGER NOT NULL,
                EventName TEXT NOT NULL,
                EventTime REAL NOT NULL,
                Detail TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS MatchSamples (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MatchId INTEGER NOT NULL REFERENCES Matches(Id),
                GameTime REAL NOT NULL,
                Kills INTEGER NOT NULL,
                Deaths INTEGER NOT NULL,
                Assists INTEGER NOT NULL,
                CreepScore INTEGER NOT NULL,
                WardScore REAL NOT NULL,
                Level INTEGER NOT NULL,
                Gold REAL NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_MatchEvents_Match_Event
                ON MatchEvents(MatchId, EventId);
            CREATE INDEX IF NOT EXISTS IX_MatchSamples_MatchId ON MatchSamples(MatchId);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public int StartMatch(string gameMode, string mapName, string champion, string position, string team)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO Matches (StartedAtUtc, GameMode, MapName, Champion, Position, Team)
            VALUES ($startedAtUtc, $gameMode, $mapName, $champion, $position, $team);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$startedAtUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$gameMode", gameMode);
        command.Parameters.AddWithValue("$mapName", mapName);
        command.Parameters.AddWithValue("$champion", champion);
        command.Parameters.AddWithValue("$position", position);
        command.Parameters.AddWithValue("$team", team);

        var id = Convert.ToInt32((long)command.ExecuteScalar()!);
        _log.Info($"Maç kaydı açıldı: #{id} {champion} ({gameMode})");

        return id;
    }

    /// <summary>
    /// Overwrites the match's running totals. Called often, so it is a plain UPDATE of the latest
    /// figures rather than an append — the per-minute history lives in <c>MatchSamples</c>.
    /// </summary>
    public void UpdateTotals(int matchId, double durationSeconds, MatchSample totals)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE Matches
            SET DurationSeconds = $duration, Kills = $kills, Deaths = $deaths, Assists = $assists,
                CreepScore = $creepScore, WardScore = $wardScore, Level = $level
            WHERE Id = $matchId;
            """;
        command.Parameters.AddWithValue("$matchId", matchId);
        command.Parameters.AddWithValue("$duration", durationSeconds);
        command.Parameters.AddWithValue("$kills", totals.Kills);
        command.Parameters.AddWithValue("$deaths", totals.Deaths);
        command.Parameters.AddWithValue("$assists", totals.Assists);
        command.Parameters.AddWithValue("$creepScore", totals.CreepScore);
        command.Parameters.AddWithValue("$wardScore", totals.WardScore);
        command.Parameters.AddWithValue("$level", totals.Level);
        command.ExecuteNonQuery();
    }

    /// <param name="result">Win, Lose or Neither; null when the game vanished without a GameEnd.</param>
    public void EndMatch(int matchId, string? result)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE Matches SET EndedAtUtc = $endedAtUtc, Result = $result WHERE Id = $matchId;
            """;
        command.Parameters.AddWithValue("$matchId", matchId);
        command.Parameters.AddWithValue("$endedAtUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$result", (object?)result ?? DBNull.Value);
        command.ExecuteNonQuery();

        _log.Info($"Maç kaydı kapandı: #{matchId} sonuç={result ?? "bilinmiyor"}");
    }

    public void SaveSample(int matchId, MatchSample sample)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO MatchSamples (MatchId, GameTime, Kills, Deaths, Assists, CreepScore, WardScore, Level, Gold)
            VALUES ($matchId, $gameTime, $kills, $deaths, $assists, $creepScore, $wardScore, $level, $gold);
            """;
        command.Parameters.AddWithValue("$matchId", matchId);
        command.Parameters.AddWithValue("$gameTime", sample.GameTime);
        command.Parameters.AddWithValue("$kills", sample.Kills);
        command.Parameters.AddWithValue("$deaths", sample.Deaths);
        command.Parameters.AddWithValue("$assists", sample.Assists);
        command.Parameters.AddWithValue("$creepScore", sample.CreepScore);
        command.Parameters.AddWithValue("$wardScore", sample.WardScore);
        command.Parameters.AddWithValue("$level", sample.Level);
        command.Parameters.AddWithValue("$gold", sample.Gold);
        command.ExecuteNonQuery();
    }

    public void SaveEvents(int matchId, IReadOnlyList<MatchEventRow> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO MatchEvents (MatchId, EventId, EventName, EventTime, Detail)
            VALUES ($matchId, $eventId, $eventName, $eventTime, $detail);
            """;

        var matchIdParam = command.Parameters.Add("$matchId", SqliteType.Integer);
        var eventIdParam = command.Parameters.Add("$eventId", SqliteType.Integer);
        var eventNameParam = command.Parameters.Add("$eventName", SqliteType.Text);
        var eventTimeParam = command.Parameters.Add("$eventTime", SqliteType.Real);
        var detailParam = command.Parameters.Add("$detail", SqliteType.Text);

        foreach (var row in events)
        {
            matchIdParam.Value = matchId;
            eventIdParam.Value = row.EventId;
            eventNameParam.Value = row.EventName;
            eventTimeParam.Value = row.EventTime;
            detailParam.Value = row.Detail;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Closes any match left open by a crash or a kill mid-game, so the next session does not append
    /// to a stale row. The result stays null: nothing was seen, and guessing one would poison the
    /// win rate more quietly than leaving a gap.
    /// </summary>
    public int CloseAbandonedMatches()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE Matches SET EndedAtUtc = $endedAtUtc WHERE EndedAtUtc IS NULL;
            """;
        command.Parameters.AddWithValue("$endedAtUtc", DateTime.UtcNow.ToString("O"));

        var closed = command.ExecuteNonQuery();

        if (closed > 0)
        {
            _log.Info($"Yarım kalmış {closed} maç kaydı kapatıldı.");
        }

        return closed;
    }

    public List<MatchRecord> GetRecentMatches(int limit = 20)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, StartedAtUtc, EndedAtUtc, GameMode, MapName, Champion, Position, Team,
                   Result, DurationSeconds, Kills, Deaths, Assists, CreepScore, WardScore, Level
            FROM Matches
            ORDER BY StartedAtUtc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        return Read(command);
    }

    /// <summary>Shared by the queries above; both select the same columns in the same order.</summary>
    private static List<MatchRecord> Read(Microsoft.Data.Sqlite.SqliteCommand command)
    {
        var results = new List<MatchRecord>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(new MatchRecord(
                Id: reader.GetInt32(0),
                StartedAtUtc: DateTime.Parse(reader.GetString(1)).ToUniversalTime(),
                EndedAtUtc: reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
                GameMode: reader.GetString(3),
                MapName: reader.GetString(4),
                Champion: reader.GetString(5),
                Position: reader.GetString(6),
                Team: reader.GetString(7),
                Result: reader.IsDBNull(8) ? null : reader.GetString(8),
                DurationSeconds: reader.GetDouble(9),
                Kills: reader.GetInt32(10),
                Deaths: reader.GetInt32(11),
                Assists: reader.GetInt32(12),
                CreepScore: reader.GetInt32(13),
                WardScore: reader.GetDouble(14),
                Level: reader.GetInt32(15)));
        }

        return results;
    }

    /// <summary>
    /// Finished matches with no recorded result.
    /// <para>
    /// In practice this is nearly all of them. The live API's <c>GameEnd</c> event never arrives
    /// before the endpoint closes with the game — a real ranked match recorded 111 events and that
    /// was not among them — so win and loss have to be filled in afterwards from the client's own
    /// match history.
    /// </para>
    /// </summary>
    public List<MatchRecord> GetUnresolvedMatches(int limit = 20)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, StartedAtUtc, EndedAtUtc, GameMode, MapName, Champion, Position, Team,
                   Result, DurationSeconds, Kills, Deaths, Assists, CreepScore, WardScore, Level
            FROM Matches
            WHERE EndedAtUtc IS NOT NULL AND Result IS NULL
            ORDER BY StartedAtUtc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        return Read(command);
    }

    /// <summary>
    /// Fills in a result found after the fact. Only ever writes to a match that has none — a result
    /// seen live is the better evidence and must not be overwritten by a later guess.
    /// </summary>
    public bool SetResult(int matchId, string result)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE Matches SET Result = $result WHERE Id = $matchId AND Result IS NULL;
            """;
        command.Parameters.AddWithValue("$matchId", matchId);
        command.Parameters.AddWithValue("$result", result);

        var written = command.ExecuteNonQuery() > 0;

        if (written)
        {
            _log.Info($"Maç sonucu tamamlandı: #{matchId} = {result}");
        }

        return written;
    }

    /// <summary>
    /// Removes one match and everything hanging off it.
    /// <para>
    /// Deleting the row alone would leave its events and samples behind, pointing at a match that
    /// no longer exists — invisible in every screen but still counted by anything that scans those
    /// tables. All three go in one transaction so a failure leaves the match intact rather than
    /// half-erased.
    /// </para>
    /// </summary>
    public bool DeleteMatch(int matchId)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        var removed = 0;

        foreach (var sql in new[]
        {
            "DELETE FROM MatchEvents WHERE MatchId = $matchId;",
            "DELETE FROM MatchSamples WHERE MatchId = $matchId;",
            "DELETE FROM Matches WHERE Id = $matchId;",
        })
        {
            using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$matchId", matchId);

            removed = command.ExecuteNonQuery();
        }

        transaction.Commit();

        // Only the last statement's count matters: it says whether the match itself existed.
        var deleted = removed > 0;

        _log.Info(deleted
            ? $"Maç kaydı silindi: #{matchId}"
            : $"Silinecek maç bulunamadı: #{matchId}");

        return deleted;
    }

    public List<MatchSample> GetSamples(int matchId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT GameTime, Kills, Deaths, Assists, CreepScore, WardScore, Level, Gold
            FROM MatchSamples WHERE MatchId = $matchId ORDER BY GameTime ASC;
            """;
        command.Parameters.AddWithValue("$matchId", matchId);

        var results = new List<MatchSample>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(new MatchSample(
                GameTime: reader.GetDouble(0),
                Kills: reader.GetInt32(1),
                Deaths: reader.GetInt32(2),
                Assists: reader.GetInt32(3),
                CreepScore: reader.GetInt32(4),
                WardScore: reader.GetDouble(5),
                Level: reader.GetInt32(6),
                Gold: reader.GetDouble(7)));
        }

        return results;
    }

    public List<MatchEventRow> GetEvents(int matchId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT EventId, EventName, EventTime, Detail
            FROM MatchEvents WHERE MatchId = $matchId ORDER BY EventTime ASC, EventId ASC;
            """;
        command.Parameters.AddWithValue("$matchId", matchId);

        var results = new List<MatchEventRow>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(new MatchEventRow(
                EventId: reader.GetInt32(0),
                EventName: reader.GetString(1),
                EventTime: reader.GetDouble(2),
                Detail: reader.GetString(3)));
        }

        return results;
    }

    /// <summary>
    /// Per-champion aggregates over finished matches.
    /// <para>
    /// Win rate is computed over matches whose result was actually recorded, which is why
    /// <see cref="ChampionStat.DecidedMatches"/> is reported alongside the total: a match closed by
    /// a crash counts towards experience but must not count as a loss.
    /// </para>
    /// <para>
    /// CS at ten minutes takes the sample nearest the ten-minute mark from below, but only from
    /// matches that actually reached it. Both halves are needed: without the second, a game that
    /// ended at eight minutes would contribute its eight-minute count as though it were a
    /// ten-minute one and quietly drag the average down. The CASE has no ELSE, so such a match
    /// yields NULL and AVG skips it rather than counting a zero.
    /// </para>
    /// </summary>
    public List<ChampionStat> GetChampionStats(int minimumMatches = 1)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT m.Champion,
                   COUNT(*),
                   SUM(CASE WHEN m.Result IS NOT NULL THEN 1 ELSE 0 END),
                   SUM(CASE WHEN m.Result = 'Win' THEN 1 ELSE 0 END),
                   AVG(m.Kills), AVG(m.Deaths), AVG(m.Assists), AVG(m.CreepScore), AVG(m.WardScore),
                   AVG(CASE WHEN EXISTS (
                            SELECT 1 FROM MatchSamples reached
                            WHERE reached.MatchId = m.Id AND reached.GameTime >= 600
                        ) THEN (
                            SELECT s.CreepScore FROM MatchSamples s
                            WHERE s.MatchId = m.Id AND s.GameTime <= 600
                            ORDER BY s.GameTime DESC LIMIT 1
                        ) END)
            FROM Matches m
            WHERE m.EndedAtUtc IS NOT NULL
            GROUP BY m.Champion
            HAVING COUNT(*) >= $minimumMatches
            ORDER BY COUNT(*) DESC, m.Champion ASC;
            """;
        command.Parameters.AddWithValue("$minimumMatches", minimumMatches);

        var results = new List<ChampionStat>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(new ChampionStat(
                Champion: reader.GetString(0),
                Matches: reader.GetInt32(1),
                DecidedMatches: reader.GetInt32(2),
                Wins: reader.GetInt32(3),
                AverageKills: reader.GetDouble(4),
                AverageDeaths: reader.GetDouble(5),
                AverageAssists: reader.GetDouble(6),
                AverageCreepScore: reader.GetDouble(7),
                AverageWardScore: reader.GetDouble(8),
                AverageCreepScoreAtTen: reader.IsDBNull(9) ? null : reader.GetDouble(9)));
        }

        return results;
    }
}
