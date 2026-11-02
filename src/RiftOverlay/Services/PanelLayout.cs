namespace RiftOverlay.Services;

/// <summary>
/// Where a panel sits, as a fraction of the overlay rather than a pixel offset. A saved pixel
/// position would put the panel somewhere else entirely the next time the game runs at a different
/// resolution; a fraction lands in the same place on any screen.
/// </summary>
public sealed class PanelPlacement
{
    public double X { get; set; }

    public double Y { get; set; }
}

/// <summary>
/// The draggable panels and where they start. Ids are stable strings because they end up in
/// settings.json — renaming one silently resets that panel to its default.
/// </summary>
public static class PanelIds
{
    public const string Status = "status";
    public const string Warnings = "warnings";
    public const string Objectives = "objectives";
    public const string Build = "build";
    public const string Lane = "lane";
    public const string Enemies = "enemies";
    public const string EnemyItems = "enemyItems";
    public const string Feed = "feed";

    /// <summary>
    /// Chosen to reproduce the original fixed layout. Warnings are the exception: they used to push
    /// the objective row down, which free positioning cannot do, so they get their own spot along
    /// the top instead of overlapping it.
    /// </summary>
    public static IReadOnlyDictionary<string, PanelPlacement> Defaults { get; } =
        new Dictionary<string, PanelPlacement>(StringComparer.Ordinal)
        {
            [Status] = new() { X = 0.015, Y = 0.020 },
            [Warnings] = new() { X = 0.300, Y = 0.020 },
            [Objectives] = new() { X = 0.015, Y = 0.065 },
            [Build] = new() { X = 0.015, Y = 0.160 },
            [Lane] = new() { X = 0.015, Y = 0.255 },
            [Enemies] = new() { X = 0.800, Y = 0.020 },
            [EnemyItems] = new() { X = 0.800, Y = 0.270 },
            [Feed] = new() { X = 0.015, Y = 0.820 },
        };

    public static PanelPlacement DefaultFor(string id) =>
        Defaults.TryGetValue(id, out var placement)
            ? new PanelPlacement { X = placement.X, Y = placement.Y }
            : new PanelPlacement { X = 0.05, Y = 0.05 };
}

public static class PanelLayout
{
    /// <summary>
    /// Turns a fraction into a pixel offset, keeping the whole panel on screen. Without the clamp a
    /// panel dragged to the edge at one resolution would hang off the side at a smaller one, and a
    /// panel that is off screen cannot be dragged back.
    /// </summary>
    public static (double Left, double Top) ToPixels(
        PanelPlacement placement, double panelWidth, double panelHeight, double hostWidth, double hostHeight)
    {
        var left = placement.X * hostWidth;
        var top = placement.Y * hostHeight;

        return (
            Math.Clamp(left, 0, Math.Max(0, hostWidth - panelWidth)),
            Math.Clamp(top, 0, Math.Max(0, hostHeight - panelHeight)));
    }

    /// <summary>Converts a dropped pixel position back into a fraction to be saved.</summary>
    public static PanelPlacement ToPlacement(double left, double top, double hostWidth, double hostHeight)
    {
        if (hostWidth <= 0 || hostHeight <= 0)
        {
            return new PanelPlacement();
        }

        return new PanelPlacement
        {
            X = Math.Clamp(left / hostWidth, 0, 1),
            Y = Math.Clamp(top / hostHeight, 0, 1),
        };
    }
}
