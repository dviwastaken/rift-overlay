using RiftOverlay.Services.Lcu;
using RiftOverlay.Services.OpGg;

namespace RiftOverlay.Client;

/// <summary>
/// Converts an OP.GG rune row into the shape the League client accepts.
/// <para>
/// It lives in the client layer rather than in either service so that neither has to know about the
/// other — the LCU code stays ignorant of where a preset came from, and the OP.GG code stays a
/// plain reader.
/// </para>
/// </summary>
public static class OpGgRuneMapper
{
    /// <summary>Four primary runes: the keystone plus its three minors.</summary>
    private const int PrimaryCount = 4;

    /// <summary>Two runes from the secondary tree.</summary>
    private const int SecondaryCount = 2;

    /// <summary>Three stat shards.</summary>
    private const int ShardCount = 3;

    /// <summary>
    /// Builds a preset, or null when the row cannot make a valid page.
    /// <para>
    /// The count check is the point of this method. The client accepts a page with the wrong number
    /// of perks without complaint and then shows it as broken in game — an empty row where a rune
    /// should be — so a short or malformed row is refused here instead of being written and
    /// discovered mid-champion-select. Refusing costs one missing button; writing costs a rune page.
    /// </para>
    /// </summary>
    public static RunePreset? ToPreset(RuneRow runes)
    {
        if (runes.PrimaryPageId <= 0 || runes.SecondaryPageId <= 0)
        {
            return null;
        }

        if (runes.PrimaryRuneIds.Count != PrimaryCount ||
            runes.SecondaryRuneIds.Count != SecondaryCount ||
            runes.StatModIds.Count != ShardCount)
        {
            return null;
        }

        // Order is not cosmetic: the client reads the list positionally — keystone, three minors,
        // two secondaries, three shards — so concatenating the groups in this order is what makes
        // the page come out as the one OP.GG described.
        return new RunePreset
        {
            PrimaryStyleId = runes.PrimaryPageId,
            SubStyleId = runes.SecondaryPageId,
            SelectedPerkIds =
            [
                .. runes.PrimaryRuneIds,
                .. runes.SecondaryRuneIds,
                .. runes.StatModIds,
            ],
        };
    }

    /// <summary>A short human summary of what would be written, for the confirmation line.</summary>
    public static string Describe(RuneRow runes) =>
        $"{runes.PrimaryPageName} + {runes.SecondaryPageName} ({runes.Play:N0} maç)";
}
