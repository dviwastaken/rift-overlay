using RiftOverlay.Services.Lcu;

namespace RiftOverlay.Services;

/// <summary>
/// Fills in the win or loss the live game never reported.
/// <para>
/// The Live Client Data API documents a <c>GameEnd</c> event and the game binary lists its three
/// values, but it does not arrive in practice: the endpoint shuts down with the game before that
/// event is ever polled. A real ranked match recorded 111 events at four polls a second and
/// <c>GameEnd</c> was not among them. Left alone, every match would be filed with no result and the
/// personal win rate would stay permanently unknown.
/// </para>
/// <para>
/// The client's own match history has the answer, so results are reconciled from there afterwards.
/// </para>
/// </summary>
public sealed class MatchResultResolver
{
    /// <summary>
    /// How far apart a recorded match and a history entry may start and still be the same game.
    /// Generous because the two clocks are unrelated — one is when the overlay first saw the game,
    /// the other is when the server created it.
    /// </summary>
    private static readonly TimeSpan StartTolerance = TimeSpan.FromHours(3);

    private readonly MatchDatabase _database;
    private readonly SummonerLookupService _lookup;
    private readonly LcuClient _client;
    private readonly ILog _log;

    public MatchResultResolver(
        MatchDatabase database, SummonerLookupService lookup, LcuClient client, ILog? log = null)
    {
        _database = database;
        _lookup = lookup;
        _client = client;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>
    /// Looks up every unresolved match and fills in what it can. Returns how many were completed.
    /// Never throws: a missing result is the state it was already in.
    /// </summary>
    public async Task<int> ResolveAsync(CancellationToken token = default)
    {
        try
        {
            var pending = _database.GetUnresolvedMatches();

            if (pending.Count == 0 || !_client.Refresh())
            {
                return 0;
            }

            var history = await _lookup.GetOwnMatchesAsync(token).ConfigureAwait(false);

            if (history.Count == 0)
            {
                return 0;
            }

            var filled = 0;
            var used = new HashSet<long>();

            foreach (var match in pending)
            {
                var candidate = FindMatch(match, history, used);

                if (candidate is null)
                {
                    continue;
                }

                used.Add(candidate.GameId);

                if (_database.SetResult(match.Id, candidate.Won ? "Win" : "Lose"))
                {
                    filled++;
                }
            }

            return filled;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log.Error("Maç sonuçları tamamlanamadı", exception);
            return 0;
        }
    }

    /// <summary>
    /// Pairs one recorded match with one history entry.
    /// <para>
    /// There is no shared id to join on — the live API never exposes the game id — so the match is
    /// made on what both sides observed independently: the champion, and the scoreline. Requiring
    /// the kills, deaths and assists to agree exactly is what makes this safe; two games on the same
    /// champion within the window are common, two with an identical scoreline are not.
    /// </para>
    /// <para>
    /// A history entry is used at most once, so back-to-back games on the same champion cannot both
    /// claim the same result.
    /// </para>
    /// </summary>
    private static MatchSummary? FindMatch(
        MatchRecord match, IReadOnlyList<MatchSummary> history, HashSet<long> used) =>
        history
            .Where(h => !used.Contains(h.GameId))
            .Where(h => string.Equals(h.Champion, match.Champion, StringComparison.OrdinalIgnoreCase))
            .Where(h => h.Kills == match.Kills && h.Deaths == match.Deaths && h.Assists == match.Assists)
            .Where(h => (h.PlayedAt.ToUniversalTime() - match.StartedAtUtc).Duration() < StartTolerance)
            // Closest in time, so a repeated scoreline still lands on the right game.
            .OrderBy(h => (h.PlayedAt.ToUniversalTime() - match.StartedAtUtc).Duration())
            .FirstOrDefault();
}
