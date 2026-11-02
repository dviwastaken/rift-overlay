using System.Windows;
using RiftOverlay.Client;
using RiftOverlay.Services;
using RiftOverlay.Services.Lcu;
using RiftOverlay.Services.OpGg;
using RiftOverlay.ViewModels;

namespace RiftOverlay;

public partial class App : Application
{
    private IGameDataSource? _source;
    private SettingsService? _settingsService;
    private LcuClient? _lcu;
    private ChampSelectService? _champSelect;
    private RunePageService? _runes;
    private ItemSetService? _itemSets;
    private SummonerLookupService? _lookup;
    private MatchDatabase? _matches;
    private MatchResultResolver? _results;

    // Lives on the application, not on the client window: champion select happens whether or not
    // that window is open, and a view model created with the window would miss everything that
    // came before it was first shown.
    private ChampSelectViewModel? _champSelectView;
    private MatchRecorder? _recorder;
    private IconCache? _icons;
    private OpGgClient? _opgg;
    private SummonerSpellCatalog? _spells;
    private TrayIcon? _tray;
    private ClientWindow? _client;
    private OverlayWindow? _overlay;
    private string? _appliedRunesFor;
    private string? _handedOffFor;
    private FileLog? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var demoMode = e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase);

        _log = new FileLog();
        _log.Info($"RiftOverlay başladı{(demoMode ? " (demo modu)" : string.Empty)}.");

        _settingsService = new SettingsService(log: _log);

        var settings = _settingsService.Load();

        // Item prices are not in the Live Client API, so the build panel waits on Data Dragon.
        var catalog = new DataDragonItemCatalog(locale: settings.ItemLocale, log: _log);

        var viewModel = new OverlayViewModel(
            demoMode ? DemoDataSource.CompressedSettings : null,
            overlaySettings: settings,
            itemCatalog: catalog,
            log: _log);

        _icons = new IconCache(log: _log);
        viewModel.AttachIcons(_icons);

        _opgg = new OpGgClient(_log);
        _spells = new SummonerSpellCatalog(locale: settings.ItemLocale, log: _log);

        LoadCatalogAsync(catalog, viewModel, _icons, _spells, _log);

        // Constructed on the UI thread so the sources can post their events straight back here.
        _source = demoMode ? new DemoDataSource() : new LiveClientService(_log);
        // Recording rides the same snapshot stream as the overlay rather than polling again. Demo
        // runs are excluded on purpose: scripted data would land in the history as real matches.
        // Kept as a field because the client statistics section reads the same database, and
        // opening a second connection to it would be a second source of truth.
        _matches = demoMode ? null : new MatchDatabase(log: _log);

        if (_matches is not null)
        {
            var database = _matches;

            _recorder = new MatchRecorder(database, _log);
            // The client needs a moment to publish the game to match history; asking the instant
            // the game closes reliably finds nothing.
            _recorder.MatchClosed += (_, _) => ResolveResultsAsync(viewModel, TimeSpan.FromSeconds(20));
        }

        _source.SnapshotReceived += (_, snapshot) =>
        {
            viewModel.OnSnapshot(snapshot);
            _recorder?.OnSnapshot(snapshot);
        };

        _source.StateChanged += (_, state) =>
        {
            viewModel.OnStateChanged(state);
            _recorder?.OnStateChanged(state);
        };

        var window = new OverlayWindow(viewModel, demoMode);
        _overlay = window;
        MainWindow = window;
        window.Show();

        _source.Start();

        // The client API is a separate connection with its own lifetime: it is up while the game
        // is not, which is exactly when champion select happens.
        _lcu = new LcuClient(_log);
        _runes = new RunePageService(_lcu, _log);
        _itemSets = new ItemSetService(_lcu, _log);
        // One champion list for the whole app: champion select and player lookup both need it, and
        // two copies meant two downloads of the same file on every start.
        var champions = new ChampionNames(_lcu, _log);

        _lookup = new SummonerLookupService(_lcu, champions, _log);

        if (_matches is not null)
        {
            _results = new MatchResultResolver(_matches, _lookup, _lcu, _log);

            // Catches up on everything recorded before this run, including matches from sessions
            // that predate result reconciliation existing at all.
            ResolveResultsAsync(viewModel, delay: TimeSpan.Zero);
        }

        // Rune presets are a champion-select action, so they moved to the client with everything
        // else that happens before a match.
        _champSelectView = new ChampSelectViewModel(_runes, settings, _icons, _log);

        _tray = new TrayIcon("RiftOverlay", _log);
        _tray.IsOverlayVisible = () => _overlay?.IsHiddenByUser == false;
        _tray.OpenClientRequested += (_, _) => ShowClient();
        _tray.ToggleOverlayRequested += (_, _) => _overlay?.ToggleVisible();
        _tray.ToggleInteractiveRequested += (_, _) => _overlay?.ToggleInteractive();
        _tray.ExitRequested += (_, _) => ExitApplication();

        _champSelect = new ChampSelectService(_lcu, champions, _log);
        _champSelect.StateChanged += (_, state) =>
        {
            // Champion select is shown in the client now, not over the game. The overlay has no
            // part in anything that happens before a match starts.
            _champSelectView!.OnState(state);
            AutoApplyRunes(settings, state);
            HandOffToClient(state);
        };

        _champSelect.PhaseChanged += (_, phase) => _champSelectView!.OnPhase(phase);
        _champSelect.Start();
    }

    /// <summary>
    /// The only way out. The client window refuses ordinary closes so it can live in the tray, so
    /// its guard is lifted first — otherwise the shutdown would be fought by the window it is
    /// trying to close.
    /// </summary>
    private void ExitApplication()
    {
        if (_client is not null)
        {
            _client.AllowClose = true;
            _client.Close();
        }

        Shutdown();
    }

    /// <summary>
    /// Shows the client window, creating it on first use. Re-opening brings the existing window
    /// forward instead of making a second one — the window closes to the tray, so it usually
    /// already exists and is merely hidden.
    /// </summary>
    private void ShowClient()
    {
        if (_lcu is null)
        {
            return;
        }

        _client ??= new ClientWindow(
            _lcu,
            new BuildViewModel(_opgg!, _spells!, _runes, _itemSets, _icons, _log),
            new PlayerViewModel(_lookup!, _icons, _log),
            // Demo runs have no recorder, so the statistics section opens its own read-only handle
            // rather than showing nothing at all.
            new StatsViewModel(_matches ?? new MatchDatabase(log: _log), _log),
            _champSelectView!);

        _client.Show();
        _client.RefreshConnection();

        if (_client.WindowState == WindowState.Minimized)
        {
            _client.WindowState = WindowState.Normal;
        }

        _client.Activate();
    }

    /// <summary>
    /// Fills in results the live game never reported, then refreshes the panel that shows them.
    /// <para>
    /// Fire-and-forget with a delay rather than awaited: this runs off a match-closed event, and
    /// nothing in the overlay should wait on the client answering.
    /// </para>
    /// </summary>
    private async void ResolveResultsAsync(OverlayViewModel viewModel, TimeSpan delay)
    {
        if (_results is null || _matches is null)
        {
            return;
        }

        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay).ConfigureAwait(true);
            }

            var filled = await _results.ResolveAsync().ConfigureAwait(true);

            if (filled > 0)
            {
            }
        }
        catch (Exception exception)
        {
            // An async void method that throws takes the process down, and a missing win-loss
            // record is not worth that.
            _log?.Error("Maç sonuçları tamamlanamadı", exception);
        }
    }


    /// <summary>
    /// Loads the locked champion's build into the client — but only if the client is already open.
    /// <para>
    /// Deliberately not opening the window: champion select is timed, and a window appearing over
    /// the game to steal focus while the user is picking would be worse than useless. If they have
    /// the client up, it follows along; if not, it stays out of the way.
    /// </para>
    /// </summary>
    private void HandOffToClient(ChampSelectState? state)
    {
        if (_client is null || !_client.IsVisible || state is null || !state.SelfLocked)
        {
            _handedOffFor = state is null ? null : _handedOffFor;
            return;
        }

        if (_handedOffFor == state.SelfChampion)
        {
            return;
        }

        _handedOffFor = state.SelfChampion;

        var position = state.Allies.FirstOrDefault(a => a.IsSelf)?.Position ?? string.Empty;

        _ = _client.ShowBuildForAsync(state.SelfChampion, position.ToLowerInvariant());
    }

    /// <summary>
    /// Applies a saved page once, when the pick is locked. Waiting for the lock matters: a hovered
    /// champion still changes, and rewriting the page for every hover would churn the user's runes
    /// throughout the whole of champion select.
    /// </summary>
    private void AutoApplyRunes(OverlaySettings settings, ChampSelectState? state)
    {
        if (_runes is null || !settings.AutoApplyRunes || state is null || !state.SelfLocked)
        {
            _appliedRunesFor = state is null ? null : _appliedRunesFor;
            return;
        }

        if (_appliedRunesFor == state.SelfChampion)
        {
            return;
        }

        var preset = settings.PresetFor(state.SelfChampion);

        if (preset is null)
        {
            return;
        }

        _appliedRunesFor = state.SelfChampion;
        _ = _runes.ApplyAsync(state.SelfChampion, preset);
    }

    /// <summary>
    /// Loads the item catalogue in the background and says so if it never arrives. Without this
    /// the build panel would just never appear, with no way to tell that from having no target.
    /// </summary>
    private static async void LoadCatalogAsync(
        DataDragonItemCatalog catalog,
        OverlayViewModel viewModel,
        IconCache icons,
        SummonerSpellCatalog spells,
        ILog log)
    {
        await catalog.LoadAsync().ConfigureAwait(true);

        if (!catalog.IsReady)
        {
            viewModel.AddWarning("Item kataloğu indirilemedi — hedef eşya paneli çalışmayacak.");
            return;
        }

        // Artwork is versioned alongside the item list, so the cache is told which patch only
        // after the catalogue has settled on one. Doing it earlier would key icons to a guess.
        icons.UseVersion(catalog.Version);

        // Same patch as the item list, for the same reason: ids are only meaningful within one.
        await spells.LoadAsync(catalog.Version).ConfigureAwait(true);

        // Only once both catalogues have settled on a patch: pruning against a version nothing has
        // loaded yet would delete the copy about to be used.
        // Logged, not silent: this is the only code in the app that deletes anything.
        CachePruner.Prune(DataDragonItemCatalog.DefaultCacheDirectory, catalog.Version, log);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Closes the row for a match still in progress when the overlay is shut down.
        _recorder?.OnStateChanged(DataSourceState.Waiting);
        _tray?.Dispose();
        _icons?.Dispose();
        _opgg?.Dispose();
        _champSelect?.Dispose();
        _lcu?.Dispose();
        _source?.Dispose();
        _settingsService?.Dispose();
        _log?.Info("RiftOverlay kapandı.");
        base.OnExit(e);
    }
}
