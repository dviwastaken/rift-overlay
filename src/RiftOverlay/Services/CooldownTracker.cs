using RiftOverlay.Models;

namespace RiftOverlay.Services;

public enum SpellSlot
{
    First,
    Second,
}

/// <param name="ReadyAtGameTime">
/// The earliest the spell can be back up. When <paramref name="IsUncertain"/> is set this assumes
/// the enemy runs Cosmic Insight, which the API does not let us confirm.
/// </param>
public sealed record SpellCooldown(
    string Player,
    SpellSlot Slot,
    string SpellName,
    double ReadyAtGameTime,
    bool IsUncertain);

/// <summary>
/// Base cooldowns and haste sources. Riot rebalances all of these, so re-check them against the
/// live patch rather than trusting the defaults indefinitely.
/// </summary>
public sealed class SummonerSpellSettings
{
    public IReadOnlyDictionary<string, double> BaseCooldowns { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Flash"] = 300,
            ["Teleport"] = 360,
            ["Ignite"] = 180,
            ["Barrier"] = 180,
            ["Heal"] = 240,
            ["Exhaust"] = 210,
            ["Ghost"] = 210,
            ["Cleanse"] = 210,
            ["Smite"] = 90,
        };

    public double DefaultCooldown { get; init; } = 240;

    /// <summary>Ionian Boots of Lucidity — visible in the enemy's item list.</summary>
    public int LucidityBootsItemId { get; init; } = 3158;

    public double LucidityBootsHaste { get; init; } = 10;

    /// <summary>Cosmic Insight sits in the Inspiration tree (rune tree id 8300).</summary>
    public int InspirationTreeId { get; init; } = 8300;

    public double CosmicInsightHaste { get; init; } = 18;
}

/// <summary>
/// Tracks summoner spells the user marks by hand. The game never reports enemy cooldowns, so the
/// start of each timer comes from a keypress — but its length is computed from what the API *does*
/// expose about that enemy (boots, rune trees) instead of assuming a flat base cooldown.
/// </summary>
public sealed class CooldownTracker
{
    private readonly SummonerSpellSettings _settings;
    private readonly Dictionary<(string Player, SpellSlot Slot), SpellCooldown> _active = new();

    public CooldownTracker(SummonerSpellSettings? settings = null) =>
        _settings = settings ?? new SummonerSpellSettings();

    /// <summary>Starts the timer, or cancels it if one is already running (undo for a mis-press).</summary>
    public void Toggle(PlayerState player, SpellSlot slot, double gameTime)
    {
        var key = (player.SummonerName, slot);

        if (_active.Remove(key))
        {
            return;
        }

        var spellName = SpellName(player, slot);
        if (string.IsNullOrWhiteSpace(spellName))
        {
            return;
        }

        var (cooldown, uncertain) = ComputeCooldown(player, spellName);

        _active[key] = new SpellCooldown(
            player.SummonerName, slot, spellName, gameTime + cooldown, uncertain);
    }

    public SpellCooldown? Get(string playerName, SpellSlot slot, double gameTime)
    {
        var key = (playerName, slot);

        if (!_active.TryGetValue(key, out var cooldown))
        {
            return null;
        }

        if (cooldown.ReadyAtGameTime <= gameTime)
        {
            _active.Remove(key);
            return null;
        }

        return cooldown;
    }

    public void Reset() => _active.Clear();

    /// <returns>
    /// The cooldown in seconds, and whether it is a lower bound. Lucidity boots are read straight
    /// off the enemy's inventory; Cosmic Insight cannot be — the API exposes only the keystone and
    /// the two tree names for other players, never their minor runes. So when the enemy runs
    /// Inspiration at all, the timer assumes they took it and is flagged uncertain.
    /// </returns>
    public (double Cooldown, bool IsUncertain) ComputeCooldown(PlayerState player, string spellName)
    {
        var baseCooldown = _settings.BaseCooldowns.TryGetValue(spellName, out var value)
            ? value
            : _settings.DefaultCooldown;

        var haste = 0d;

        if (player.Items.Any(i => i.ItemId == _settings.LucidityBootsItemId))
        {
            haste += _settings.LucidityBootsHaste;
        }

        var runsInspiration =
            player.Runes.PrimaryRuneTree.Id == _settings.InspirationTreeId ||
            player.Runes.SecondaryRuneTree.Id == _settings.InspirationTreeId;

        if (runsInspiration)
        {
            haste += _settings.CosmicInsightHaste;
        }

        return (baseCooldown * 100d / (100d + haste), runsInspiration);
    }

    public static string SpellName(PlayerState player, SpellSlot slot) => slot == SpellSlot.First
        ? player.SummonerSpells.SummonerSpellOne.DisplayName
        : player.SummonerSpells.SummonerSpellTwo.DisplayName;
}
