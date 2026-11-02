using System.Text.Json.Serialization;

namespace RiftOverlay.Services.OpGg;

/// <summary>
/// One recommendation row. <see cref="Play"/> is not optional decoration: a build that won five of
/// six games reads as a 83% winner until the sample is shown next to it, and the live capture that
/// prompted this project found exactly that at Challenger tier. Every surface that displays one of
/// these must display the count with it.
/// </summary>
public sealed class BuildRow
{
    public List<int> Ids { get; set; } = [];

    /// <summary>
    /// Localised names when the server has them. The summoner-spell row uses the same wire shape
    /// but repeats the numeric ids here instead of names, which is why this goes through
    /// <see cref="NumberTolerantStringListConverter"/> rather than being a plain string list.
    /// </summary>
    [JsonPropertyName("ids_names")]
    [JsonConverter(typeof(NumberTolerantStringListConverter))]
    public List<string> Names { get; set; } = [];

    /// <summary>How many games this exact combination was played in.</summary>
    public int Play { get; set; }

    public int Win { get; set; }

    [JsonPropertyName("pick_rate")]
    public double PickRate { get; set; }

    /// <summary>Null rather than zero when nothing was played, so "no data" cannot read as "0%".</summary>
    public double? WinRate => Play == 0 ? null : (double)Win / Play;
}

public sealed class SkillRow
{
    /// <summary>Levelling order as "Q", "W", "E", "R" — fifteen entries for a full order.</summary>
    public List<string> Order { get; set; } = [];

    /// <summary>Max priority uses this instead: three entries, e.g. Q then W then E.</summary>
    public List<string> Ids { get; set; } = [];

    /// <summary>
    /// Alternative levelling orders, most played first. The wire shape of each entry is this same
    /// class minus <see cref="Ids"/>, which is why the type repeats rather than gaining a sibling.
    /// </summary>
    public List<SkillRow> Builds { get; set; } = [];

    public int Play { get; set; }

    public int Win { get; set; }

    [JsonPropertyName("pick_rate")]
    public double PickRate { get; set; }

    public double? WinRate => Play == 0 ? null : (double)Win / Play;
}

public sealed class RuneRow
{
    [JsonPropertyName("primary_page_id")]
    public int PrimaryPageId { get; set; }

    [JsonPropertyName("primary_page_name")]
    public string PrimaryPageName { get; set; } = string.Empty;

    [JsonPropertyName("primary_rune_ids")]
    public List<int> PrimaryRuneIds { get; set; } = [];

    [JsonPropertyName("primary_rune_names")]
    public List<string> PrimaryRuneNames { get; set; } = [];

    [JsonPropertyName("secondary_page_id")]
    public int SecondaryPageId { get; set; }

    [JsonPropertyName("secondary_page_name")]
    public string SecondaryPageName { get; set; } = string.Empty;

    [JsonPropertyName("secondary_rune_ids")]
    public List<int> SecondaryRuneIds { get; set; } = [];

    [JsonPropertyName("secondary_rune_names")]
    public List<string> SecondaryRuneNames { get; set; } = [];

    [JsonPropertyName("stat_mod_ids")]
    public List<int> StatModIds { get; set; } = [];

    public int Play { get; set; }

    public int Win { get; set; }

    [JsonPropertyName("pick_rate")]
    public double PickRate { get; set; }

    public double? WinRate => Play == 0 ? null : (double)Win / Play;
}

public sealed class CounterRow
{
    [JsonPropertyName("champion_id")]
    public int ChampionId { get; set; }

    [JsonPropertyName("champion_name")]
    public string ChampionName { get; set; } = string.Empty;

    public int Play { get; set; }

    public int Win { get; set; }

    /// <summary>
    /// Whose win rate this is depends on the list it came from, which is why the two lists are kept
    /// apart rather than merged: in <c>weak_counters</c> it belongs to the counter pick, not to the
    /// champion being looked up.
    /// </summary>
    [JsonPropertyName("win_rate")]
    public double WinRate { get; set; }

    /// <summary>The looked-up champion's win rate in this matchup — unambiguous in both lists.</summary>
    [JsonPropertyName("my_win_rate")]
    public double MyWinRate { get; set; }

    /// <summary>The other champion's win rate in this matchup.</summary>
    [JsonPropertyName("counter_win_rate")]
    public double CounterWinRate { get; set; }
}

/// <summary>
/// The server's explanation for an empty counter list. It is injected rather than requested — asking
/// for it in the field list is rejected — and its presence is the difference between "no champion
/// counters this one" and "not enough games to say".
/// </summary>
public sealed class CountersMeta
{
    public string Message { get; set; } = string.Empty;
}

public sealed class ChampionSummaryStats
{
    public int Play { get; set; }

    [JsonPropertyName("win_rate")]
    public double WinRate { get; set; }

    [JsonPropertyName("pick_rate")]
    public double PickRate { get; set; }

    [JsonPropertyName("ban_rate")]
    public double BanRate { get; set; }

    public double Kda { get; set; }
}

/// <summary>
/// Where a champion stands in its lane. <see cref="Tier"/> is OP.GG's own bucket (1 is best) and
/// <see cref="Rank"/> the placement inside the position, both of which are opinions rather than
/// measurements — they are shown as the site's label, never restated as a fact about the champion.
/// </summary>
public sealed class TierData
{
    public int Tier { get; set; }

    public int Rank { get; set; }
}

public sealed class PositionStats
{
    public int Play { get; set; }

    [JsonPropertyName("win_rate")]
    public double WinRate { get; set; }

    [JsonPropertyName("pick_rate")]
    public double PickRate { get; set; }

    /// <summary>Share of this champion's games played in this position.</summary>
    [JsonPropertyName("role_rate")]
    public double RoleRate { get; set; }

    [JsonPropertyName("ban_rate")]
    public double BanRate { get; set; }

    [JsonPropertyName("tier_data")]
    public TierData? TierData { get; set; }
}

public sealed class PositionBlock
{
    public string Name { get; set; } = string.Empty;

    public PositionStats Stats { get; set; } = new();
}

public sealed class ChampionSummaryBlock
{
    public int Id { get; set; }

    [JsonPropertyName("is_rotation")]
    public bool IsRotation { get; set; }

    [JsonPropertyName("is_rip")]
    public bool IsRip { get; set; }

    [JsonPropertyName("average_stats")]
    public ChampionSummaryStats AverageStats { get; set; } = new();

    /// <summary>
    /// Per-position breakdown. The averages above blend every lane a champion is played in, so a
    /// mid pick that also sees jungle play carries a win rate belonging to neither.
    /// </summary>
    public List<PositionBlock> Positions { get; set; } = [];
}

/// <summary>
/// The <c>data</c> block of a champion analysis.
/// <para>
/// Note that <see cref="CoreItems"/>, <see cref="Boots"/>, <see cref="StarterItems"/> and
/// <see cref="SummonerSpells"/> all arrive as the same shape in the wire format and are told apart
/// only by position — see <see cref="OpGgResponseParser"/>. Keeping them as four named properties
/// here is what stops that ambiguity from leaking any further into the app.
/// </para>
/// </summary>
public sealed class ChampionAnalysis
{
    public ChampionSummaryBlock Summary { get; set; } = new();

    /// <summary>"AD", "AP" or "BOTH".</summary>
    [JsonPropertyName("damage_type")]
    public string DamageType { get; set; } = string.Empty;

    [JsonPropertyName("core_items")]
    public BuildRow CoreItems { get; set; } = new();

    public BuildRow Boots { get; set; } = new();

    /// <summary>
    /// Fourth, fifth and sixth item options, most played first.
    /// <para>
    /// These thin out fast and the thinning is the point: a live capture had three games behind the
    /// most popular sixth item. They arrive on the wire under the same class name as
    /// <see cref="CoreItems"/> and are told apart only by position, exactly like boots and starters.
    /// </para>
    /// </summary>
    [JsonPropertyName("fourth_items")]
    public List<BuildRow> FourthItems { get; set; } = [];

    [JsonPropertyName("fifth_items")]
    public List<BuildRow> FifthItems { get; set; } = [];

    [JsonPropertyName("sixth_items")]
    public List<BuildRow> SixthItems { get; set; } = [];

    /// <summary>
    /// Requested and parsed, deliberately not displayed. Its samples run seven times larger than
    /// <see cref="CoreItems"/> and its entries overlap the core only partly, so what a slot here
    /// actually denotes is a guess — and a guessed label on a recommendation is worse than a
    /// missing one.
    /// </summary>
    [JsonPropertyName("last_items")]
    public List<BuildRow> LastItems { get; set; } = [];

    [JsonPropertyName("starter_items")]
    public BuildRow StarterItems { get; set; } = new();

    [JsonPropertyName("summoner_spells")]
    public BuildRow SummonerSpells { get; set; } = new();

    public RuneRow Runes { get; set; } = new();

    public SkillRow Skills { get; set; } = new();

    [JsonPropertyName("skill_masteries")]
    public SkillRow SkillMasteries { get; set; } = new();

    /// <summary>Champions that beat this one.</summary>
    [JsonPropertyName("weak_counters")]
    public List<CounterRow> WeakCounters { get; set; } = [];

    [JsonPropertyName("strong_counters")]
    public List<CounterRow> StrongCounters { get; set; } = [];

    [JsonPropertyName("counters_meta")]
    public CountersMeta? CountersMeta { get; set; }
}

public sealed class ChampionAnalysisEnvelope
{
    public ChampionAnalysis Data { get; set; } = new();
}
