using System.Net.Http;
using System.Text.Json;
using RiftOverlay.Models;

namespace RiftOverlay.Services;

/// <summary>
/// Polls Riot's official Live Client Data API, which the game itself serves on loopback while a
/// match is in progress. Read-only and entirely outside the game process, which is what keeps
/// this approach clear of anti-cheat: nothing is injected, hooked or read from game memory.
/// </summary>
public sealed class LiveClientService : IGameDataSource
{
    private const string Endpoint = "https://127.0.0.1:2999/liveclientdata/allgamedata";

    private static readonly TimeSpan ConnectedInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan WaitingInterval = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _client;
    private readonly CancellationTokenSource _cts = new();
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly ILog _log;

    private DataSourceState _state = DataSourceState.Waiting;

    public LiveClientService(ILog? log = null)
    {
        _log = log ?? NullLog.Instance;

        // The endpoint presents Riot's own self-signed certificate. It is loopback-only, so
        // skipping the chain check here does not widen any attack surface.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(2),
        };
    }

    public event EventHandler<GameSnapshot>? SnapshotReceived;

    public event EventHandler<DataSourceState>? StateChanged;

    public void Start() => _ = Task.Run(() => PollLoopAsync(_cts.Token));

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var snapshot = await TryReadAsync(token).ConfigureAwait(false);

            if (snapshot is not null)
            {
                SetState(DataSourceState.Connected);
                Post(() => SnapshotReceived?.Invoke(this, snapshot));
            }
            else
            {
                SetState(DataSourceState.Waiting);
            }

            try
            {
                await Task.Delay(
                    _state == DataSourceState.Connected ? ConnectedInterval : WaitingInterval,
                    token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<GameSnapshot?> TryReadAsync(CancellationToken token)
    {
        try
        {
            var json = await _client.GetStringAsync(Endpoint, token).ConfigureAwait(false);
            var snapshot = JsonSerializer.Deserialize<GameSnapshot>(json, SerializerOptions);

            // During champion loading the endpoint answers with a skeleton payload; treating that
            // as "connected" would flash an empty overlay, so wait for real players.
            return snapshot is { AllPlayers.Count: > 0 } ? snapshot : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // No game running (connection refused), the client shutting down mid-request, or a
            // payload shape this build does not know yet — all just mean "no data this tick".
            return null;
        }
    }

    /// <summary>
    /// Only state <em>changes</em> are logged. Between games the endpoint refuses a connection
    /// every couple of seconds; writing a line for each would bury every real event in the file.
    /// </summary>
    private void SetState(DataSourceState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        _log.Info(state == DataSourceState.Connected ? "Maça bağlanıldı." : "Maç bitti, bekleniyor.");
        Post(() => StateChanged?.Invoke(this, state));
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

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _client.Dispose();
    }
}
