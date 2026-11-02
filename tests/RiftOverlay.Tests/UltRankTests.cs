using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

/// <summary>
/// The enemy row's ultimate indicator.
/// <para>
/// This shows <em>availability</em>, not a cooldown, and that is a deliberate limit rather than an
/// unfinished feature. Level is readable, so rank is a fact. A countdown would have to assume the
/// ability haste sitting in the enemy's minor runes — Transcendence and Ultimate Hunter are both
/// invisible to the API — and a wrong assumption there errs in the dangerous direction: the overlay
/// would report an ultimate still down while it is already back up.
/// </para>
/// </summary>
public sealed class UltRankTests
{
    private static RespawnItem Enemy(int level) => new("Ahri", "Oyuncu#TR1") { Level = level };

    [Theory]
    [InlineData(1, 0)]
    [InlineData(5, 0)]
    [InlineData(6, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(15, 2)]
    [InlineData(16, 3)]
    [InlineData(18, 3)]
    public void RankFollowsTheLevelUnlocks(int level, int expected) =>
        Assert.Equal(expected, Enemy(level).UltRank);

    [Fact]
    public void BeforeSixTheChipIsUnlitAndUnnumbered()
    {
        var enemy = Enemy(5);

        Assert.Equal("R", enemy.UltLabel);
        Assert.Equal(Palette.Muted, enemy.UltBrush);
    }

    [Fact]
    public void FromSixOnwardsTheChipShowsTheRank()
    {
        var enemy = Enemy(11);

        Assert.Equal("R2", enemy.UltLabel);
        Assert.Equal(Palette.Spell, enemy.UltBrush);
    }

    [Fact]
    public void LevellingUpRefreshesTheBoundProperties()
    {
        // The row is bound rather than rebuilt each tick, so without these notifications an enemy
        // hitting six would keep showing an unlit chip until the whole list was replaced.
        var enemy = Enemy(5);
        var raised = new List<string>();

        enemy.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        enemy.Level = 6;

        Assert.Contains(nameof(RespawnItem.UltRank), raised);
        Assert.Contains(nameof(RespawnItem.UltLabel), raised);
        Assert.Contains(nameof(RespawnItem.UltBrush), raised);
    }

    [Fact]
    public void SettingTheSameLevelRaisesNothing()
    {
        var enemy = Enemy(9);
        var raised = 0;

        enemy.PropertyChanged += (_, _) => raised++;
        enemy.Level = 9;

        Assert.Equal(0, raised);
    }
}
