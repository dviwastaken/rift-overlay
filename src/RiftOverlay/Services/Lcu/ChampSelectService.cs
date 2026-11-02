using System.Text.Json.Serialization;

namespace RiftOverlay.Services.Lcu;

public sealed class ChampSelectSession
{
    public int LocalPlayerCellId { get; set; }

    public List<ChampSelectPlayer> MyTeam { get; set; } = [];

    public List<ChampSelectPlayer> TheirTeam { get; set; } = [];

    public List<List<ChampSelectAction>> Actions { get; set; } = [];

    public ChampSelectBans Bans { get; set; } = new();

    public ChampSelectTimer Timer { get; set; } = new();
}

/// <summary>
/// The session carries bans in their own field rather than only as completed actions — more direct
/// than filtering the action list, and it is the field named for exactly this.
/// </summary>
public sealed class ChampSelectBans
{
    public List<int> MyTeamBans { get; set; } = [];

    public List<int> TheirTeamBans { get; set; } = [];

    public int NumBans { get; set; }
}

public sealed class ChampSelectPlayer
{
    public int CellId { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Set while a player is still hovering rather than locked in.</summary>
    public int ChampionPickIntent { get; set; }

    public string AssignedPosition { get; set; } = string.Empty;

    /// <summary>The champion actually shown: a locked pick, otherwise what they are hovering.</summary>
    public int VisibleChampionId => ChampionId != 0 ? ChampionId : ChampionPickIntent;
}

public sealed class ChampSelectAction
{
    public int Id { get; set; }

    public int ActorCellId { get; set; }

    public int ChampionId { get; set; }

    public bool Completed { get; set; }

    public bool IsAllyAction { get; set; }

    /// <summary>"pick" or "ban".</summary>
    public string Type { get; set; } = string.Empty;
}

public sealed class ChampSelectTimer
{
    public long AdjustedTimeLeftInPhase { get; set; }

    public string Phase { get; set; } = string.Empty;
}

public sealed class ChampionSummary
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <param name="Phase">Client phase: None, Lobby, Matchmaking, ChampSelect, InProgress…</param>
public sealed record ChampSelectState(
    string Phase,
    IReadOnlyList<ChampSelectPick> Allies,
    IReadOnlyList<ChampSelectPick> Enemies,
    IReadOnlyList<string> EnemyBans,
    int SecondsLeft,
    string SelfChampion,
    bool SelfLocked);

/// <summary>
/// One seat in the draft. <see cref="ChampionId"/> travels alongside the name because artwork is
/// filed under an id, and a champion's displayed name cannot be turned into its artwork name —
/// Wukong is filed as MonkeyKing, Kai'Sa as Kaisa, Nunu ve Willump as Nunu.
/// </summary>
public sealed record ChampSelectPick(
    string Champion, string Position, bool IsLocked, bool IsSelf, int ChampionId = 0);

/// <summary>
/// Follows the client through champion select.
/// <para>
/// Polls rather than subscribing over WebSocket: this is a handful of requests per second against
/// loopback for the couple of minutes champion select lasts, and it avoids a second connection
/// style with its own reconnect and backpressure handling for no visible gain.
/// </para>
/// <para>
/// Session shape verified against a live champion select on 2026-09-02: <c>localPlayerCellId</c>,
/// the <c>timer</c> block in milliseconds, both team arrays and the nested <c>actions</c> all match.
/// The hover-versus-locked split was confirmed the same way — a hovered champion arrived as
/// <c>championPickIntent: 523</c> while <c>championId</c> stayed 0. Custom games leave
/// <c>assignedPosition</c> empty for every player, which is why lane matching needs a manual override.
/// </para>
/// </summary>
public sealed class ChampSelectService : IDisposable
{
    private const string PhasePath = "/lol-gameflow/v1/gameflow-phase";
    private const string SessionPath = "/lol-champ-select/v1/session";

    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(1);

    private readonly LcuClient _client;
    private readonly ILog _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    private readonly ChampionNames _champions;

    private string _phase = "None";

    public ChampSelectService(LcuClient client, ChampionNames champions, ILog? log = null)
    {
        _client = client;
        _champions = champions;
        _log = log ?? NullLog.Instance;
    }

    public event EventHandler<ChampSelectState?>? StateChanged;

    /// <summary>
    /// The client phase, whatever it is — Lobby, Matchmaking, ChampSelect, InProgress, None.
    /// <para>
    /// Separate from <see cref="StateChanged"/> because that one only ever carries a champion
    /// select, and everything before champion select is now shown too: a client sitting in a lobby
    /// or a queue is information, not an empty screen.
    /// </para>
    /// </summary>
    public event EventHandler<string>? PhaseChanged;

    public void Start() => _ = Task.Run(() => LoopAsync(_cts.Token));

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var inChampSelect = false;

            try
            {
                if (_client.Refresh())
                {
                    inChampSelect = await PollAsync(token).ConfigureAwait(false);
                }
                else if (_phase != "None")
                {
                    _phase = "None";
                    Post(() => StateChanged?.Invoke(this, null));
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _log.Error("Champion select yoklamasi başarısız", exception);
            }

            try
            {
                await Task.Delay(inChampSelect ? ActiveInterval : IdleInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> PollAsync(CancellationToken token)
    {
        var phase = await _client.GetAsync<string>(PhasePath, token).ConfigureAwait(false) ?? "None";

        if (phase != _phase)
        {
            _log.Info($"İstemci durumu: {phase}");
            _phase = phase;

            var announced = phase;
            Post(() => PhaseChanged?.Invoke(this, announced));
        }

        if (phase != "ChampSelect")
        {
            Post(() => StateChanged?.Invoke(this, null));
            return false;
        }

        await _champions.EnsureLoadedAsync(token).ConfigureAwait(false);

        var session = await _client.GetAsync<ChampSelectSession>(SessionPath, token).ConfigureAwait(false);

        if (session is null)
        {
            return true;
        }

        var state = Describe(session);
        Post(() => StateChanged?.Invoke(this, state));

        return true;
    }

    public ChampSelectState Describe(ChampSelectSession session)
    {
        var self = session.MyTeam.FirstOrDefault(p => p.CellId == session.LocalPlayerCellId);

        var bans = EnemyBans(session);

        return new ChampSelectState(
            "ChampSelect",
            [.. session.MyTeam.Select(p => ToPick(p, session.LocalPlayerCellId))],
            [.. session.TheirTeam.Select(p => ToPick(p, session.LocalPlayerCellId))],
            bans,
            (int)Math.Max(0, session.Timer.AdjustedTimeLeftInPhase / 1000),
            NameOf(self?.VisibleChampionId ?? 0),
            self?.ChampionId is > 0);
    }

    /// <summary>
    /// Prefers the session's own <c>bans</c> field over reconstructing bans from the action list.
    /// A live capture found the field present but empty — Practice Tool has no ban phase at all — so
    /// the action scan stays as a fallback rather than being deleted on the strength of one sighting.
    /// </summary>
    private List<string> EnemyBans(ChampSelectSession session)
    {
        var declared = session.Bans.TheirTeamBans.Where(id => id > 0).ToList();

        if (declared.Count > 0)
        {
            return [.. declared.Select(NameOf)];
        }

        return
        [
            .. session.Actions
                .SelectMany(group => group)
                .Where(a => a.Type == "ban" && a.Completed && !a.IsAllyAction && a.ChampionId > 0)
                .Select(a => NameOf(a.ChampionId))
        ];
    }

    private ChampSelectPick ToPick(ChampSelectPlayer player, int localCellId) => new(
        NameOf(player.VisibleChampionId),
        player.AssignedPosition,
        player.ChampionId != 0,
        player.CellId == localCellId,
        player.VisibleChampionId);

    private string NameOf(int championId) => _champions.NameOf(championId);

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private void Post(Action action)
    {
        if (_context is null)
        {
            action();
        }
        else
        {
            _context.Post(_ => action(), null);
        }
    }
}
