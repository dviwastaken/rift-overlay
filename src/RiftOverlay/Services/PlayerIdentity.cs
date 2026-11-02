using RiftOverlay.Models;

namespace RiftOverlay.Services;

/// <summary>
/// The one place that knows how the live payload names people.
/// <para>
/// This exists because the naming rule is not obvious and getting it wrong fails quietly. Every
/// player field carries the full Riot ID — <c>summonerName</c> is <c>"Ornek#0001"</c>, not
/// <c>"Ornek"</c> — while the event feed names people bare. There is no single rule either: one live
/// capture read <c>{"KillerName":"Sion Bot","VictimName":"Oyuncu"}</c>, so bots are named the way
/// <c>summonerName</c> spells them and humans the way <c>riotIdGameName</c> does. Comparing the two
/// directly matches nothing, and the failure looks like "team attribution just doesn't work" rather
/// than like a bug.
/// </para>
/// <para>
/// Both sides therefore get reduced to the part before the <c>#</c>, and all the name fields are
/// tried. Every caller must go through here rather than re-deriving it.
/// </para>
/// </summary>
public static class PlayerIdentity
{
    /// <summary>The bare game name — what the event feed uses. "?" when there is nothing to show.</summary>
    public static string Short(string? riotId) =>
        string.IsNullOrWhiteSpace(riotId) ? "?" : riotId.Split('#')[0];

    /// <summary>True when an event's name field refers to this player, whoever spelled it.</summary>
    public static bool Matches(PlayerState player, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var bare = Short(name);

        return Equal(Short(player.RiotIdGameName), bare)
            || Equal(Short(player.RiotId), bare)
            || Equal(Short(player.SummonerName), bare);
    }

    /// <summary>
    /// The active player's full row. Only <c>allPlayers</c> carries items, runes and spells, so
    /// anything about the user's own loadout has to come through here rather than off
    /// <c>activePlayer</c>, which exposes gold and live stats but no inventory.
    /// </summary>
    public static PlayerState? ResolveSelf(GameSnapshot snapshot)
    {
        var activeName = snapshot.ActivePlayer?.RiotIdGameName;

        if (string.IsNullOrWhiteSpace(activeName))
        {
            activeName = snapshot.ActivePlayer?.SummonerName;
        }

        return string.IsNullOrWhiteSpace(activeName)
            ? null
            : snapshot.AllPlayers.FirstOrDefault(p => Matches(p, activeName));
    }

    private static bool Equal(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
