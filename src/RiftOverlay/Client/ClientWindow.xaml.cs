using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using RiftOverlay.Services.Lcu;

namespace RiftOverlay.Client;

public enum ClientSection
{
    ChampSelect,
    Player,
    Build,
    Stats,
}

/// <summary>
/// The companion window: everything that belongs outside the game — player lookup and reading build
/// recommendations, with writing them back to the client to follow.
/// <para>
/// It closes to the tray rather than shutting down. The overlay keeps running while this window is
/// gone, so <see cref="OnClosing"/> cancels the close and hides instead; the process only ends from
/// the tray menu.
/// </para>
/// </summary>
public partial class ClientWindow : Window
{
    private readonly LcuClient _lcu;
    private readonly BuildViewModel _build;
    private readonly PlayerViewModel _player;
    private readonly StatsViewModel _stats;
    private readonly ChampSelectViewModel _champSelect;

    public ClientWindow(
        LcuClient lcu,
        BuildViewModel build,
        PlayerViewModel player,
        StatsViewModel stats,
        ChampSelectViewModel champSelect)
    {
        _lcu = lcu;
        _build = build;
        _player = player;
        _stats = stats;
        _champSelect = champSelect;

        InitializeComponent();

        // Scoped per pane rather than window-wide: the sections have unrelated view models, and one
        // shared context would make every binding ambiguous as more sections are added.
        BuildPane.DataContext = _build;
        PlayerPane.DataContext = _player;
        StatsPane.DataContext = _stats;
        ChampSelectPane.DataContext = _champSelect;
    }

    /// <summary>
    /// Lets the application close this window for real on the way out. Without it the tray's
    /// Exit would be fought by the close-to-tray handler below and the window would refuse to go.
    /// </summary>
    public bool AllowClose { get; set; }

    /// <summary>
    /// Refreshes the connection line. Called when the window is shown rather than polled: the
    /// window is only interesting while someone is looking at it.
    /// </summary>
    public void RefreshConnection()
    {
        var connected = _lcu.Refresh();

        ConnectionText.Text = connected ? "İstemciye bağlı" : "İstemci kapalı";
    }

    /// <summary>
    /// Selects a section from outside — the tray, or later a champion-select hand-off that wants to
    /// land the user straight on the build for the champion they just locked.
    /// </summary>
    public void ShowSection(ClientSection section)
    {
        NavChampSelect.IsChecked = section == ClientSection.ChampSelect;
        NavPlayer.IsChecked = section == ClientSection.Player;
        NavBuild.IsChecked = section == ClientSection.Build;
        NavStats.IsChecked = section == ClientSection.Stats;
    }

    private void OnNavChanged(object sender, RoutedEventArgs e)
    {
        // Null-guarded because Checked fires while the XAML is still being parsed, before the
        // panes below have been created.
        if (PlayerPane is null || BuildPane is null || StatsPane is null || ChampSelectPane is null)
        {
            return;
        }

        ChampSelectPane.Visibility = NavChampSelect.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PlayerPane.Visibility = NavPlayer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        BuildPane.Visibility = NavBuild.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        StatsPane.Visibility = NavStats.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        // Read from disk when the section is opened rather than on a timer: these rows only change
        // when a match ends, and the overlay is what ends matches.
        if (NavStats.IsChecked == true)
        {
            _stats.Refresh();
        }
    }

    private async void OnSearchPlayer(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void OnPlayerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SearchAsync();
        }
    }

    private async Task SearchAsync()
    {
        try
        {
            await _player.SearchAsync();
        }
        catch (Exception)
        {
            // Reported through the view model status line.
        }
    }

    private async void OnFetchBuild(object sender, RoutedEventArgs e) => await FetchAsync();

    private async void OnApplyRunes(object sender, RoutedEventArgs e)
    {
        try
        {
            await _build.ApplyRunesAsync();
        }
        catch (Exception)
        {
            // Reported through the view model's status line; an escape from an async void handler
            // would take the whole process with it.
        }
    }

    private async void OnApplyItems(object sender, RoutedEventArgs e)
    {
        try
        {
            await _build.ApplyItemsAsync();
        }
        catch (Exception)
        {
            // Reported through the view model status line.
        }
    }

    /// <summary>
    /// Opens the client on a champion's build. Champion select calls this when a pick is locked, so
    /// the user does not retype a champion the client already knows about.
    /// </summary>
    public async Task ShowBuildForAsync(string champion, string position)
    {
        ShowSection(ClientSection.Build);

        try
        {
            await _build.LoadForAsync(champion, position);
        }
        catch (Exception)
        {
            // Same reasoning as above.
        }
    }

    /// <summary>Enter in the champion box does the same as the button — it is the obvious gesture.</summary>
    private async void OnChampionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await FetchAsync();
        }
    }

    /// <summary>
    /// Wraps the fetch so neither async void handler can let an exception escape. An unhandled one
    /// from an async void method terminates the process, and this window is not worth the overlay.
    /// </summary>
    private async Task FetchAsync()
    {
        try
        {
            await _build.LoadAsync();
        }
        catch (Exception)
        {
            // The view model already reports failures through its own status line.
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
