using RiftOverlay.Models;

namespace RiftOverlay.Services;

/// <param name="CreepScoreDifference">Positive when the player is ahead.</param>
public sealed record LaneComparison(
    PlayerState Opponent,
    int SelfCreepScore,
    int OpponentCreepScore,
    int SelfLevel,
    int OpponentLevel,
    bool IsManual)
{
    public int CreepScoreDifference => SelfCreepScore - OpponentCreepScore;

    public int LevelDifference => SelfLevel - OpponentLevel;
}

/// <summary>
/// Works out which enemy the player is actually up against.
/// <para>
/// The obvious answer is to match on <c>position</c>, and that works for the enemy team — but a
/// live capture came back with <c>NONE</c> for the human player while every bot had a real lane.
/// So position is used when it is there and a manual pick takes over when it is not, rather than
/// the panel simply failing to appear.
/// </para>
/// </summary>
public sealed class LaneMatchupResolver
{
    private const string NoPosition = "NONE";

    /// <summary>Summoner name of a hand-picked opponent; null returns to matching on position.</summary>
    public string? ManualOpponent { get; set; }

    public LaneComparison? Resolve(PlayerState? self, IReadOnlyList<PlayerState> enemies)
    {
        if (self is null || enemies.Count == 0)
        {
            return null;
        }

        var manual = ManualOpponent is null
            ? null
            : enemies.FirstOrDefault(e => e.SummonerName == ManualOpponent);

        var opponent = manual ?? MatchOnPosition(self, enemies);

        return opponent is null ? null : new LaneComparison(
            opponent,
            self.Scores.CreepScore,
            opponent.Scores.CreepScore,
            self.Level,
            opponent.Level,
            manual is not null);
    }

    private static PlayerState? MatchOnPosition(PlayerState self, IReadOnlyList<PlayerState> enemies)
    {
        if (!HasPosition(self))
        {
            return null;
        }

        return enemies.FirstOrDefault(e =>
            HasPosition(e) && string.Equals(e.Position, self.Position, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasPosition(PlayerState player) =>
        !string.IsNullOrWhiteSpace(player.Position) &&
        !string.Equals(player.Position, NoPosition, StringComparison.OrdinalIgnoreCase);
}
