using RiftOverlay.Models;

namespace RiftOverlay.Services;

/// <summary>
/// Turns the live snapshot stream into rows in <see cref="MatchDatabase"/>.
/// <para>
/// The live API has no match id, so match identity has to be inferred. Two signals do it: the
/// clock, which only ever moves forward inside one game, and the connection dropping. A clock that
/// jumps backwards is a different game; a clock that has advanced since the last snapshot is the
/// same one, even if the overlay was restarted in between. That second case matters — reconnecting
/// after an alt-tab crash mid-game must resume the open match rather than open a second row for it.
/// </para>
/// <para>
/// Snapshots arrive four times a second. Writing all of them would put roughly seven thousand rows
/// in the database per game to describe a curve that moves in minutes, so samples are taken on a
/// game-clock interval instead. Events are not downsampled: they are the part worth keeping exactly.
/// </para>
/// </summary>
public sealed class MatchRecorder
{
    /// <summary>Game-clock seconds between stat samples. A 30 minute game costs 120 rows.</summary>
    public static readonly double SampleIntervalSeconds = 15;

    /// <summary>
    /// A clock that went back further than this means a new game. The tolerance exists because the
    /// clock is a double sampled over a network hop and can wobble by a hair between polls; without
    /// it, a jitter of a few milliseconds would look like a new match.
    /// </summary>
    private const double RewindToleranceSeconds = 5;

    private readonly MatchDatabase _database;
    private readonly ILog _log;

    private int? _matchId;
    private double _lastGameTime = -1;
    private double _lastSampleTime = double.NegativeInfinity;
    private int _lastEventId = -1;
    private string? _pendingResult;

    public MatchRecorder(MatchDatabase database, ILog? log = null)
    {
        _database = database;
        _log = log ?? NullLog.Instance;

        // A match still open here belongs to a previous run that never closed it.
        _database.CloseAbandonedMatches();
    }

    /// <summary>The row currently being written to, or null when no match is being recorded.</summary>
    public int? CurrentMatchId => _matchId;

    /// <summary>
    /// Raised once a match has been written and closed. The history panel listens for this rather
    /// than polling, because a closed match is the only thing that can change what it shows.
    /// </summary>
    public event EventHandler? MatchClosed;

    public void OnSnapshot(GameSnapshot snapshot)
    {
        var self = PlayerIdentity.ResolveSelf(snapshot);

        if (self is null)
        {
            return;
        }

        var gameTime = snapshot.GameData.GameTime;

        if (_matchId is not null && gameTime < _lastGameTime - RewindToleranceSeconds)
        {
            _log.Info($"Oyun saati geri gitti ({_lastGameTime:F0}s → {gameTime:F0}s), yeni maç sayılıyor.");
            Close(null);
        }

        _matchId ??= Start(snapshot, self);
        _lastGameTime = gameTime;

        RecordEvents(snapshot, gameTime);
        RecordSample(snapshot, self, gameTime);

        if (_pendingResult is not null)
        {
            Close(_pendingResult);
        }
    }

    /// <summary>
    /// Closes the match when the game goes away. A GameEnd event usually lands first and carries the
    /// result; when it does not — the client was killed, or the overlay started mid-game and missed
    /// it — the match is still closed, with no result rather than a guessed one.
    /// </summary>
    public void OnStateChanged(DataSourceState state)
    {
        if (state == DataSourceState.Waiting && _matchId is not null)
        {
            Close(_pendingResult);
        }
    }

    private int Start(GameSnapshot snapshot, PlayerState self)
    {
        _lastSampleTime = double.NegativeInfinity;
        _lastEventId = -1;
        _pendingResult = null;

        return _database.StartMatch(
            snapshot.GameData.GameMode,
            snapshot.GameData.MapName,
            self.ChampionName,
            self.Position,
            self.Team);
    }

    private void Close(string? result)
    {
        var closed = _matchId is not null;

        if (_matchId is not null)
        {
            _database.EndMatch(_matchId.Value, result);
        }

        _matchId = null;
        _lastGameTime = -1;
        _lastSampleTime = double.NegativeInfinity;
        _lastEventId = -1;
        _pendingResult = null;

        if (closed)
        {
            MatchClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Appends events not seen yet. The feed is cumulative — every poll repeats the whole match — so
    /// the id guard is what keeps a 40 minute game from being written 10,000 times over.
    /// </summary>
    private void RecordEvents(GameSnapshot snapshot, double gameTime)
    {
        var fresh = snapshot.Events.Events.Where(e => e.EventId > _lastEventId).ToList();

        if (fresh.Count == 0)
        {
            return;
        }

        _lastEventId = fresh.Max(e => e.EventId);

        _database.SaveEvents(_matchId!.Value, [.. fresh.Select(e =>
            new MatchEventRow(e.EventId, e.EventName, e.EventTime, Detail(e)))]);

        var ended = fresh.FirstOrDefault(e => e.EventName == "GameEnd");

        if (ended is not null)
        {
            // Held rather than closed here: the snapshot carrying GameEnd also carries the final
            // scoreboard, and closing first would drop the last kill from the totals.
            _pendingResult = ended.MatchResult;
            _log.Info($"GameEnd görüldü, sonuç: {_pendingResult ?? "alan okunamadı"} (t={gameTime:F0}s)");
        }
    }

    private void RecordSample(GameSnapshot snapshot, PlayerState self, double gameTime)
    {
        if (gameTime - _lastSampleTime < SampleIntervalSeconds)
        {
            return;
        }

        _lastSampleTime = gameTime;

        var sample = new MatchSample(
            GameTime: gameTime,
            Kills: self.Scores.Kills,
            Deaths: self.Scores.Deaths,
            Assists: self.Scores.Assists,
            CreepScore: self.Scores.CreepScore,
            WardScore: self.Scores.WardScore,
            Level: self.Level,
            Gold: snapshot.ActivePlayer?.CurrentGold ?? 0);

        _database.SaveSample(_matchId!.Value, sample);
        _database.UpdateTotals(_matchId.Value, gameTime, sample);
    }

    /// <summary>
    /// A short readable summary, so the stored timeline is legible without re-deriving it from the
    /// event name. Everything the API gives for that event kind, nothing invented.
    /// </summary>
    private static string Detail(GameEvent gameEvent) => gameEvent.EventName switch
    {
        "ChampionKill" => $"{PlayerIdentity.Short(gameEvent.KillerName)} → {PlayerIdentity.Short(gameEvent.VictimName)}",
        "Multikill" => $"{PlayerIdentity.Short(gameEvent.KillerName)} ×{gameEvent.KillStreak}",
        "FirstBlood" => PlayerIdentity.Short(gameEvent.Recipient),
        "DragonKill" => $"{gameEvent.DragonType} — {PlayerIdentity.Short(gameEvent.KillerName)}",
        "BaronKill" or "HeraldKill" or "HordeKill" => PlayerIdentity.Short(gameEvent.KillerName),
        "Ace" => $"{PlayerIdentity.Short(gameEvent.Acer)} ({gameEvent.AcingTeam})",
        "TurretKilled" => $"{gameEvent.TurretKilled} — {PlayerIdentity.Short(gameEvent.KillerName)}",
        "InhibKilled" => $"{gameEvent.InhibKilled} — {PlayerIdentity.Short(gameEvent.KillerName)}",
        "InhibRespawningSoon" => gameEvent.InhibRespawningSoon ?? string.Empty,
        "InhibRespawned" => gameEvent.InhibRespawned ?? string.Empty,
        "GameEnd" => gameEvent.MatchResult ?? string.Empty,
        _ => string.Empty,
    };
}
