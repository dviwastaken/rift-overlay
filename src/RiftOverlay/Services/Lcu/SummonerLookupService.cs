using System.Text.Json.Serialization;

namespace RiftOverlay.Services.Lcu;

/// <param name="Queue">Queue key as the client names it, e.g. RANKED_SOLO_5x5.</param>
/// <param name="Losses">
/// Reported by the endpoint but, in practice, always zero — see <see cref="WinRate"/>.
/// </param>
public sealed record RankEntry(string Queue, string Tier, string Division, int LeaguePoints, int Wins, int Losses)
{
    public bool IsRanked => !string.IsNullOrWhiteSpace(Tier) && !string.Equals(Tier, "NONE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The win rate, or null when it cannot be known.
    /// <para>
    /// <b>This is null far more often than it looks like it should be.</b> The client's ranked
    /// endpoint carries a <c>losses</c> field and then leaves it at zero: an account sitting at
    /// Silver I came back with 163 wins and 0 losses, in both the queue map and the queues array.
    /// Dividing by wins plus losses therefore produces exactly 100% for every ranked player alive,
    /// which is a fabricated number rather than a measured one.
    /// </para>
    /// <para>
    /// So a rate is only offered when there is at least one recorded loss. A genuine unbeaten
    /// record is hidden by this rule too, and that is the right trade: hiding something true is
    /// recoverable, printing something false is not.
    /// </para>
    /// </summary>
    public double? WinRate => Losses == 0 ? null : (double)Wins / (Wins + Losses);

    /// <summary>Total games, or null when losses are not reported and the total is unknowable.</summary>
    public int? Games => Losses == 0 ? null : Wins + Losses;
}

public sealed record MatchSummary(
    long GameId,
    string Champion,
    string Mode,
    bool Won,
    int Kills,
    int Deaths,
    int Assists,
    int CreepScore,
    int Level,
    TimeSpan Duration,
    DateTime PlayedAt,
    /// <summary>Carried alongside the name because artwork is filed by id, never by name.</summary>
    int ChampionId = 0);

public sealed record PlayerProfile(
    string GameName,
    string TagLine,
    int Level,
    string Puuid,
    IReadOnlyList<RankEntry> Ranks,
    IReadOnlyList<MatchSummary> Matches)
{
    public string RiotId => $"{GameName}#{TagLine}";
}

// ---- Wire shapes, captured from a live client ------------------------------------------------

internal sealed class AliasLookup
{
    public AliasName Alias { get; set; } = new();

    public string Puuid { get; set; } = string.Empty;
}

internal sealed class AliasName
{
    public string GameName { get; set; } = string.Empty;

    public string TagLine { get; set; } = string.Empty;
}

internal sealed class SummonerRecord
{
    public string GameName { get; set; } = string.Empty;

    public string TagLine { get; set; } = string.Empty;

    public int SummonerLevel { get; set; }

    public string Puuid { get; set; } = string.Empty;
}

internal sealed class RankedStats
{
    /// <summary>Keyed by queue name — RANKED_SOLO_5x5, RANKED_FLEX_SR, and the TFT queues.</summary>
    public Dictionary<string, RankedQueue> QueueMap { get; set; } = [];
}

internal sealed class RankedQueue
{
    public string QueueType { get; set; } = string.Empty;

    public string Tier { get; set; } = string.Empty;

    public string Division { get; set; } = string.Empty;

    public int LeaguePoints { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }
}

internal sealed class MatchHistoryResponse
{
    public MatchHistoryGames Games { get; set; } = new();
}

internal sealed class MatchHistoryGames
{
    public List<LcuMatch> Games { get; set; } = [];
}

/// <summary>
/// One game as the client reports it. Named apart from the recorder MatchRecord on purpose: that
/// one is our own database row, this is a wire shape, and they describe different things.
/// </summary>
internal sealed class LcuMatch
{
    public long GameId { get; set; }

    public string GameMode { get; set; } = string.Empty;

    public long GameCreation { get; set; }

    public int GameDuration { get; set; }

    public List<ParticipantIdentity> ParticipantIdentities { get; set; } = [];

    public List<Participant> Participants { get; set; } = [];
}

internal sealed class ParticipantIdentity
{
    public int ParticipantId { get; set; }

    public IdentityPlayer Player { get; set; } = new();
}

internal sealed class IdentityPlayer
{
    public string Puuid { get; set; } = string.Empty;
}

internal sealed class Participant
{
    public int ParticipantId { get; set; }

    public int ChampionId { get; set; }

    public ParticipantStats Stats { get; set; } = new();
}

internal sealed class ParticipantStats
{
    public bool Win { get; set; }

    public int Kills { get; set; }

    public int Deaths { get; set; }

    public int Assists { get; set; }

    public int ChampLevel { get; set; }

    public int TotalMinionsKilled { get; set; }

    public int NeutralMinionsKilled { get; set; }
}

/// <summary>
/// Looks up any player through the League client.
/// <para>
/// This runs entirely on the LCU — no Riot Web API key, no rate-limited third party. That was not
/// assumed: the three endpoints were tried against another player's puuid before any of this was
/// written, because the whole feature rests on them answering for someone other than the logged-in
/// user, and building on that untested would have been a large wasted detour.
/// </para>
/// <para>
/// A Riot ID is resolved through <c>alias/lookup</c>; the older name-based endpoint now rejects
/// requests outright.
/// </para>
/// </summary>
public sealed class SummonerLookupService
{
    private const int MatchCount = 10;

    private readonly LcuClient _client;
    private readonly ChampionNames _champions;
    private readonly ILog _log;

    public SummonerLookupService(LcuClient client, ChampionNames champions, ILog? log = null)
    {
        _client = client;
        _champions = champions;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>
    /// Resolves "name#tag" to a profile, or null when the client is closed or the id is unknown.
    /// The tag is optional; without one the current player's own region tag is assumed.
    /// </summary>
    public async Task<PlayerProfile?> FindAsync(string riotId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(riotId))
        {
            return null;
        }

        var parts = riotId.Split('#', 2, StringSplitOptions.TrimEntries);
        var gameName = parts[0];
        var tagLine = parts.Length > 1 ? parts[1] : string.Empty;

        if (string.IsNullOrWhiteSpace(gameName))
        {
            return null;
        }

        var puuid = await ResolvePuuidAsync(gameName, tagLine, token).ConfigureAwait(false);

        if (puuid is null)
        {
            return null;
        }

        return await LoadAsync(puuid, token).ConfigureAwait(false);
    }

    /// <summary>Loads a profile for a puuid already in hand — from a match, or from champion select.</summary>
    public async Task<PlayerProfile?> LoadAsync(string puuid, CancellationToken token = default)
    {
        await _champions.EnsureLoadedAsync(token).ConfigureAwait(false);

        var summoner = await _client
            .GetAsync<SummonerRecord>($"/lol-summoner/v2/summoners/puuid/{puuid}", token)
            .ConfigureAwait(false);

        if (summoner is null)
        {
            _log.Warn("Oyuncu özeti okunamadı.");
            return null;
        }

        var ranks = await LoadRanksAsync(puuid, token).ConfigureAwait(false);
        var matches = await LoadMatchesAsync(puuid, token).ConfigureAwait(false);

        return new PlayerProfile(
            summoner.GameName,
            summoner.TagLine,
            summoner.SummonerLevel,
            puuid,
            ranks,
            matches);
    }

    /// <summary>
    /// The signed-in player's own recent matches.
    /// <para>
    /// Separate from <see cref="LoadAsync"/> because the caller that needs this — filling in results
    /// the live game never reported — has no use for rank or a profile header, and asking for them
    /// would be two extra requests per reconciliation.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<MatchSummary>> GetOwnMatchesAsync(CancellationToken token = default)
    {
        var summoner = await _client
            .GetAsync<SummonerRecord>("/lol-summoner/v1/current-summoner", token)
            .ConfigureAwait(false);

        if (summoner is null || string.IsNullOrWhiteSpace(summoner.Puuid))
        {
            return [];
        }

        await _champions.EnsureLoadedAsync(token).ConfigureAwait(false);

        return await LoadMatchesAsync(summoner.Puuid, token).ConfigureAwait(false);
    }

    private async Task<string?> ResolvePuuidAsync(string gameName, string tagLine, CancellationToken token)
    {
        var query = $"gameName={Uri.EscapeDataString(gameName)}";

        if (!string.IsNullOrWhiteSpace(tagLine))
        {
            query += $"&tagLine={Uri.EscapeDataString(tagLine)}";
        }

        var alias = await _client
            .GetAsync<AliasLookup>($"/lol-summoner/v1/alias/lookup?{query}", token)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(alias?.Puuid) ? null : alias.Puuid;
    }

    /// <summary>
    /// Only the Summoner's Rift ranked queues. The map also carries the TFT ladders, which are a
    /// different game and would read as a second rank for this one.
    /// </summary>
    private async Task<List<RankEntry>> LoadRanksAsync(string puuid, CancellationToken token)
    {
        var stats = await _client
            .GetAsync<RankedStats>($"/lol-ranked/v1/ranked-stats/{puuid}", token)
            .ConfigureAwait(false);

        if (stats is null)
        {
            return [];
        }

        string[] wanted = ["RANKED_SOLO_5x5", "RANKED_FLEX_SR"];

        return [.. wanted
            .Where(stats.QueueMap.ContainsKey)
            .Select(key => stats.QueueMap[key])
            .Select(q => new RankEntry(q.QueueType, q.Tier, q.Division, q.LeaguePoints, q.Wins, q.Losses))
            .Where(r => r.IsRanked)];
    }

    private async Task<List<MatchSummary>> LoadMatchesAsync(string puuid, CancellationToken token)
    {
        var history = await _client
            .GetAsync<MatchHistoryResponse>(
                $"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex=0&endIndex={MatchCount}", token)
            .ConfigureAwait(false);

        if (history is null)
        {
            return [];
        }

        var summaries = new List<MatchSummary>();

        foreach (var game in history.Games.Games)
        {
            var summary = Summarise(game, puuid);

            if (summary is not null)
            {
                summaries.Add(summary);
            }
        }

        return summaries;
    }

    /// <summary>
    /// Picks this player's row out of a match.
    /// <para>
    /// The two halves are linked by <c>participantId</c>, not by order: identities and participants
    /// are separate arrays and nothing promises they line up. Indexing one by the other's position
    /// would attribute someone else's game to the player being looked at.
    /// </para>
    /// </summary>
    private MatchSummary? Summarise(LcuMatch game, string puuid)
    {
        var identity = game.ParticipantIdentities
            .FirstOrDefault(i => string.Equals(i.Player.Puuid, puuid, StringComparison.OrdinalIgnoreCase));

        if (identity is null)
        {
            return null;
        }

        var participant = game.Participants.FirstOrDefault(p => p.ParticipantId == identity.ParticipantId);

        if (participant is null)
        {
            return null;
        }

        var stats = participant.Stats;

        return new MatchSummary(
            game.GameId,
            _champions.NameOf(participant.ChampionId),
            game.GameMode,
            stats.Win,
            stats.Kills,
            stats.Deaths,
            stats.Assists,
            // Jungle camps count towards CS and are reported separately.
            stats.TotalMinionsKilled + stats.NeutralMinionsKilled,
            stats.ChampLevel,
            TimeSpan.FromSeconds(game.GameDuration),
            DateTimeOffset.FromUnixTimeMilliseconds(game.GameCreation).LocalDateTime,
            participant.ChampionId);
    }
}
