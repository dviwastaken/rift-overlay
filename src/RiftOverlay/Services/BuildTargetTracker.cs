using RiftOverlay.Models;

namespace RiftOverlay.Services;

/// <param name="Progress">Gold already sunk into the recipe, as components on the board.</param>
/// <param name="Remaining">Gold still to earn after spending what is in the wallet right now.</param>
/// <param name="IsAmbiguous">
/// True when the owned components fit more than one finished item equally well — a lone Sheen
/// builds into four different things, and guessing silently would be worse than saying so.
/// </param>
public sealed record BuildTarget(
    ItemInfo Item,
    int Progress,
    int Remaining,
    double Completion,
    bool IsManual,
    bool IsAmbiguous);

/// <summary>
/// Works out which item the player is building toward and how much gold is left to finish it.
/// Inference is a guess from the components already owned; a manual pick always wins over it.
/// </summary>
public sealed class BuildTargetTracker
{
    private readonly IItemCatalog _catalog;

    public BuildTargetTracker(IItemCatalog catalog) => _catalog = catalog;

    /// <summary>Set from the picker; null returns to inferring the target from the inventory.</summary>
    public int? ManualTargetId { get; set; }

    public BuildTarget? Resolve(IReadOnlyList<PlayerItem> owned, double currentGold)
    {
        if (!_catalog.IsReady)
        {
            return null;
        }

        var ownedIds = owned.Select(i => i.ItemId).ToList();

        if (ManualTargetId is { } manualId && _catalog.TryGet(manualId, out var manual))
        {
            return Build(manual, ownedIds, currentGold, isManual: true, isAmbiguous: false);
        }

        var ranked = _catalog.BuildTargets
            .Where(target => !ownedIds.Contains(target.Id))
            .Select(target => (Target: target, Progress: MatchedGold(target, ownedIds)))
            .Where(candidate => candidate.Progress > 0)
            // Closest by gold already committed; ties go to the cheaper finish.
            .OrderByDescending(candidate => candidate.Progress)
            .ThenBy(candidate => candidate.Target.TotalGold)
            .Take(2)
            .ToList();

        if (ranked.Count == 0)
        {
            return null;
        }

        var best = ranked[0];
        var ambiguous = ranked.Count > 1 && ranked[1].Progress == best.Progress;

        return Build(best.Target, ownedIds, currentGold, isManual: false, isAmbiguous: ambiguous);
    }

    /// <summary>Finished items the owned components could still lead to, best fit first.</summary>
    public IReadOnlyList<ItemInfo> Candidates(IReadOnlyList<PlayerItem> owned, int limit = 6)
    {
        if (!_catalog.IsReady)
        {
            return [];
        }

        var ownedIds = owned.Select(i => i.ItemId).ToList();

        return
        [
            .. _catalog.BuildTargets
                .Where(target => !ownedIds.Contains(target.Id))
                .Select(target => (Target: target, Progress: MatchedGold(target, ownedIds)))
                .Where(candidate => candidate.Progress > 0)
                .OrderByDescending(candidate => candidate.Progress)
                .ThenBy(candidate => candidate.Target.TotalGold)
                .Take(limit)
                .Select(candidate => candidate.Target),
        ];
    }

    private BuildTarget Build(
        ItemInfo target, List<int> ownedIds, double currentGold, bool isManual, bool isAmbiguous)
    {
        var progress = MatchedGold(target, ownedIds);
        var remaining = Math.Max(0, target.TotalGold - progress - (int)currentGold);
        var completion = target.TotalGold <= 0
            ? 0
            : Math.Clamp((progress + currentGold) / target.TotalGold, 0, 1);

        return new BuildTarget(target, progress, remaining, completion, isManual, isAmbiguous);
    }

    /// <summary>
    /// Gold value of the owned items that sit somewhere in this recipe. Each owned item is spent
    /// once, and matching a component stops the walk there — otherwise a Sheen would be counted
    /// both as itself and again as the parts it was built from.
    /// </summary>
    public int MatchedGold(ItemInfo target, IEnumerable<int> ownedIds)
    {
        var available = new List<int>(ownedIds);

        return target.From.Sum(component => Walk(component, available));
    }

    private int Walk(int itemId, List<int> available)
    {
        if (available.Remove(itemId))
        {
            return _catalog.TryGet(itemId, out var owned) ? owned.TotalGold : 0;
        }

        if (!_catalog.TryGet(itemId, out var item))
        {
            return 0;
        }

        return item.From.Sum(component => Walk(component, available));
    }
}
