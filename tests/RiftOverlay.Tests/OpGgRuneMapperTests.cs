using RiftOverlay.Client;
using RiftOverlay.Services.OpGg;

namespace RiftOverlay.Tests;

/// <summary>
/// Converting an OP.GG rune row into a page the client will accept.
/// <para>
/// The refusals matter more than the happy path here. Applying a page deletes and recreates one in
/// the League client with no undo, so a malformed row has to be turned away before it is written —
/// the client takes a wrong-length page without complaint and only shows it as broken in game.
/// </para>
/// </summary>
public sealed class OpGgRuneMapperTests
{
    private static RuneRow Row() => new()
    {
        PrimaryPageId = 8200,
        PrimaryPageName = "Büyücülük",
        PrimaryRuneIds = [8214, 8226, 8210, 8237],
        SecondaryPageId = 8000,
        SecondaryPageName = "İsabet",
        SecondaryRuneIds = [8009, 8014],
        StatModIds = [5005, 5008, 5001],
        Play = 9532,
        Win = 4280,
    };

    [Fact]
    public void ACompleteRowBecomesAPage()
    {
        var preset = OpGgRuneMapper.ToPreset(Row());

        Assert.NotNull(preset);
        Assert.Equal(8200, preset!.PrimaryStyleId);
        Assert.Equal(8000, preset.SubStyleId);
        Assert.True(preset.IsUsable);
    }

    [Fact]
    public void PerksKeepTheirOrder_KeystoneFirstThenSecondaryThenShards()
    {
        // The client reads the list positionally; a shuffled list produces a different page than
        // the one OP.GG described, silently.
        var preset = OpGgRuneMapper.ToPreset(Row());

        Assert.Equal([8214, 8226, 8210, 8237, 8009, 8014, 5005, 5008, 5001], preset!.SelectedPerkIds);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void AWrongNumberOfPrimaryRunesIsRefused(int count)
    {
        var row = Row();
        row.PrimaryRuneIds = [.. Enumerable.Range(8200, count)];

        Assert.Null(OpGgRuneMapper.ToPreset(row));
    }

    [Fact]
    public void MissingShardsAreRefused()
    {
        var row = Row();
        row.StatModIds = [5005];

        Assert.Null(OpGgRuneMapper.ToPreset(row));
    }

    [Fact]
    public void AnEmptyRowIsRefused() => Assert.Null(OpGgRuneMapper.ToPreset(new RuneRow()));

    [Fact]
    public void TheSummaryNamesBothTreesAndTheSample()
    {
        // Shown beside the apply button: the write is irreversible, so what is about to happen has
        // to be legible before it happens — including how many games back it.
        Assert.Equal("Büyücülük + İsabet (9.532 maç)", OpGgRuneMapper.Describe(Row()));
    }
}
