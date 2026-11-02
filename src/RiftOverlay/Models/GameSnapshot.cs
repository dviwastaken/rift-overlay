using System.Text.Json.Serialization;

namespace RiftOverlay.Models;

/// <summary>
/// Shape of GET https://127.0.0.1:2999/liveclientdata/allgamedata.
/// Only the fields the overlay actually consumes are modelled; the payload carries
/// considerably more (full rune pages, ability ranks, per-item detail).
/// </summary>
public sealed class GameSnapshot
{
    public ActivePlayer? ActivePlayer { get; set; }

    public List<PlayerState> AllPlayers { get; set; } = [];

    public EventContainer Events { get; set; } = new();

    public GameStats GameData { get; set; } = new();
}

public sealed class ActivePlayer
{
    public string SummonerName { get; set; } = string.Empty;

    public string RiotIdGameName { get; set; } = string.Empty;

    public int Level { get; set; }

    public double CurrentGold { get; set; }

    /// <summary>Only the active player exposes live stats; enemies never do.</summary>
    public ChampionStats ChampionStats { get; set; } = new();
}

public sealed class ChampionStats
{
    public double AbilityHaste { get; set; }

    public double AttackDamage { get; set; }

    public double AbilityPower { get; set; }
}

public sealed class PlayerState
{
    public string ChampionName { get; set; } = string.Empty;

    /// <summary>
    /// Despite the name this carries the full Riot ID ("Ornek#0001"), same as <see cref="RiotId"/>.
    /// Event feeds report the bare game name instead, so never compare the two directly.
    /// </summary>
    public string SummonerName { get; set; } = string.Empty;

    public string RiotId { get; set; } = string.Empty;

    /// <summary>The bare name without the tag — this is what event fields like KillerName use.</summary>
    public string RiotIdGameName { get; set; } = string.Empty;

    public string RiotIdTagLine { get; set; } = string.Empty;

    public bool IsBot { get; set; }

    public string Position { get; set; } = string.Empty;

    /// <summary>"ORDER" (blue side) or "CHAOS" (red side).</summary>
    public string Team { get; set; } = string.Empty;

    public int Level { get; set; }

    public bool IsDead { get; set; }

    /// <summary>Seconds left until this champion respawns; 0 while alive.</summary>
    public double RespawnTimer { get; set; }

    public PlayerScores Scores { get; set; } = new();

    public SummonerSpells SummonerSpells { get; set; } = new();

    /// <summary>Current inventory. Available for every player, not just the active one.</summary>
    public List<PlayerItem> Items { get; set; } = [];

    /// <summary>Keystone and rune trees. Also exposed for every player.</summary>
    public PlayerRunes Runes { get; set; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(RiotId) ? SummonerName : RiotId.Split('#')[0];
}

public sealed class PlayerItem
{
    [JsonPropertyName("itemID")]
    public int ItemId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public int Count { get; set; }

    public int Price { get; set; }

    public int Slot { get; set; }
}

public sealed class PlayerRunes
{
    public RuneInfo Keystone { get; set; } = new();

    public RuneInfo PrimaryRuneTree { get; set; } = new();

    public RuneInfo SecondaryRuneTree { get; set; } = new();
}

public sealed class RuneInfo
{
    public int Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;
}

public sealed class PlayerScores
{
    public int Kills { get; set; }

    public int Deaths { get; set; }

    public int Assists { get; set; }

    public int CreepScore { get; set; }

    /// <summary>Vision score. Read straight off the API schema table, beside the other scores.</summary>
    public double WardScore { get; set; }
}

public sealed class SummonerSpells
{
    public SummonerSpell SummonerSpellOne { get; set; } = new();

    public SummonerSpell SummonerSpellTwo { get; set; } = new();
}

public sealed class SummonerSpell
{
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class EventContainer
{
    [JsonPropertyName("Events")]
    public List<GameEvent> Events { get; set; } = [];
}

/// <summary>
/// One entry of the live event feed. The feed is heterogeneous — every event carries
/// EventID/EventName/EventTime, and the remaining fields depend on EventName.
/// </summary>
public sealed class GameEvent
{
    [JsonPropertyName("EventID")]
    public int EventId { get; set; }

    public string EventName { get; set; } = string.Empty;

    public double EventTime { get; set; }

    public string? KillerName { get; set; }

    public string? VictimName { get; set; }

    public string? DragonType { get; set; }

    public string? Stolen { get; set; }

    public string? TurretKilled { get; set; }

    /// <summary>
    /// Structure id such as "Inhib_TChaos_L1_P1_1931666598_0". Each inhibitor event carries the id
    /// in a field named after the event itself, so all three have to be modelled separately —
    /// reading InhibKilled on an InhibRespawned event yields null.
    /// </summary>
    public string? InhibKilled { get; set; }

    public string? InhibRespawningSoon { get; set; }

    public string? InhibRespawned { get; set; }

    public string? Recipient { get; set; }

    /// <summary>
    /// GameEnd only. The three possible values — Win, Lose, Neither — are confirmed from the API
    /// schema table in the game binary, but the <em>field name</em> is not: no "Result" string
    /// appears in that table. Every structure event carries its payload in a field named after the
    /// event itself, so <c>GameEnd</c> is at least as likely a name as <c>Result</c>. Both are read
    /// and <see cref="MatchResult"/> takes whichever arrives, rather than betting on one.
    /// </summary>
    public string? Result { get; set; }

    [JsonPropertyName("GameEnd")]
    public string? GameEndResult { get; set; }

    /// <summary>Win, Lose or Neither once a GameEnd event lands; null on every other event.</summary>
    public string? MatchResult =>
        string.IsNullOrWhiteSpace(Result) ? NullIfBlank(GameEndResult) : Result;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>How many kills the Multikill event covers.</summary>
    public int KillStreak { get; set; }

    /// <summary>Ace only: who landed it, and which team aced.</summary>
    public string? Acer { get; set; }

    public string? AcingTeam { get; set; }

    public List<string> Assisters { get; set; } = [];
}

public sealed class GameStats
{
    public string GameMode { get; set; } = string.Empty;

    /// <summary>Seconds since the gates opened, matching the in-game clock.</summary>
    public double GameTime { get; set; }

    public string MapName { get; set; } = string.Empty;
}
