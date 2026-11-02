using RiftOverlay.Models;

namespace RiftOverlay.Services;

/// <param name="ItemId">Carried so the row can show artwork; icon URLs are keyed by id, not name.</param>
public sealed record ItemCompletion(
    string Player, string Champion, int ItemId, string ItemName, int Cost, double GameTime);

/// <summary>
/// Notices when an enemy finishes a significant item — the moment their power actually steps up.
/// <para>
/// "Significant" is the item's own gold cost rather than a curated list of scary items: a hand
/// written list would need revisiting every patch and would quietly go stale, while the catalogue
/// already knows what each item costs.
/// </para>
/// </summary>
public sealed class ItemWatcher
{
    private readonly IItemCatalog _catalog;
    private readonly int _minimumCost;
    private readonly Dictionary<string, HashSet<int>> _seen = new(StringComparer.OrdinalIgnoreCase);

    public ItemWatcher(IItemCatalog catalog, int minimumCost = 2500)
    {
        _catalog = catalog;
        _minimumCost = minimumCost;
    }

    public void Reset() => _seen.Clear();

    /// <summary>
    /// Completions since the previous call. The first time a player is seen their inventory is
    /// only recorded, never reported — otherwise attaching to a game in progress would announce
    /// six items per enemy that were bought long ago.
    /// </summary>
    public IReadOnlyList<ItemCompletion> Observe(IEnumerable<PlayerState> players, double gameTime)
    {
        if (!_catalog.IsReady)
        {
            return [];
        }

        var completions = new List<ItemCompletion>();

        foreach (var player in players)
        {
            var current = player.Items.Select(i => i.ItemId).ToHashSet();

            if (!_seen.TryGetValue(player.SummonerName, out var previous))
            {
                _seen[player.SummonerName] = current;
                continue;
            }

            foreach (var itemId in current.Where(id => !previous.Contains(id)))
            {
                if (IsSignificant(itemId, out var item))
                {
                    completions.Add(new ItemCompletion(
                        player.SummonerName, player.ChampionName, item.Id, item.Name, item.TotalGold, gameTime));
                }
            }

            _seen[player.SummonerName] = current;
        }

        return completions;
    }

    private bool IsSignificant(int itemId, out ItemInfo item) =>
        _catalog.TryGet(itemId, out item!) && item.IsBuildTarget && item.TotalGold >= _minimumCost;
}
