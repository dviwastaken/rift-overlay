using System.Collections.ObjectModel;
using System.Windows.Media;
using RiftOverlay.Services;
using RiftOverlay.Services.Lcu;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Client;

/// <param name="Record">
/// Wins only when the client did not report losses — see <see cref="RankEntry.WinRate"/>.
/// </param>
public sealed record RankLine(string Queue, string Tier, string Record);

/// <summary>
/// One row of the recent-match list. The portrait is part of the value, exactly as in champion
/// select, so a row that gains its artwork is simply a different row.
/// </summary>
public sealed record MatchLine(
    string Result,
    string Champion,
    string Score,
    string CreepScore,
    string Duration,
    string Mode,
    string When,
    bool Won,
    ImageSource? Portrait,
    int ChampionId);

/// <summary>
/// Player lookup by Riot ID, backed entirely by the League client.
/// <para>
/// Everything shown here is read; nothing is written. The endpoints were confirmed to answer for
/// accounts other than the logged-in one before any of this existed, because the feature is
/// pointless if they only work for yourself.
/// </para>
/// </summary>
public sealed class PlayerViewModel : ObservableObject
{
    private readonly SummonerLookupService _lookup;
    private readonly IconCache? _icons;
    private readonly ILog _log;

    private string _query = string.Empty;
    private string _status = "Riot ID yaz: isim#etiket";
    private string _headline = string.Empty;
    private bool _isBusy;
    private bool _hasProfile;

    public PlayerViewModel(SummonerLookupService lookup, IconCache? icons = null, ILog? log = null)
    {
        _lookup = lookup;
        _icons = icons;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>The portrait if the cache already holds it; otherwise the fetch starts.</summary>
    private ImageSource? Portrait(int championId)
    {
        if (_icons is null || championId <= 0)
        {
            return null;
        }

        _icons.TryGetChampionIcon(championId, out var icon);

        return icon;
    }

    /// <summary>
    /// Fills in the artwork the rows were built without, replacing each row as its picture arrives.
    /// <para>
    /// The rows are records, so a row is replaced rather than mutated — which keeps them comparable
    /// by value the way champion select's are, and still updates the list. Sequential rather than
    /// parallel, for the same reason the item icons are: ten rows off somebody else's CDN is a
    /// queue, not a burst.
    /// </para>
    /// </summary>
    public async Task LoadPortraitsAsync()
    {
        if (_icons is null)
        {
            return;
        }

        for (var index = 0; index < Matches.Count; index++)
        {
            var line = Matches[index];

            if (line.Portrait is not null || line.ChampionId <= 0)
            {
                continue;
            }

            try
            {
                var image = await _icons.GetChampionIconAsync(line.ChampionId).ConfigureAwait(true);

                if (image is not null && index < Matches.Count && Matches[index] == line)
                {
                    Matches[index] = line with { Portrait = image };
                }
            }
            catch (Exception exception)
            {
                _log.Warn($"Portre yüklenemedi: {exception.Message}");
            }
        }
    }

    public ObservableCollection<RankLine> Ranks { get; } = [];

    public ObservableCollection<MatchLine> Matches { get; } = [];

    public string Query
    {
        get => _query;
        set => Set(ref _query, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Riot ID and level, once a profile is loaded.</summary>
    public string Headline
    {
        get => _headline;
        private set => Set(ref _headline, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public bool HasProfile
    {
        get => _hasProfile;
        private set => Set(ref _hasProfile, value);
    }

    public async Task SearchAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(Query))
        {
            return;
        }

        IsBusy = true;
        Status = "Aranıyor…";

        try
        {
            var profile = await _lookup.FindAsync(Query.Trim()).ConfigureAwait(true);

            if (profile is null)
            {
                Clear();

                // Two very different causes, one message: we cannot tell a closed client from an
                // unknown Riot ID without guessing, and guessing wrong is worse than being vague.
                Status = "Bulunamadı — Riot ID yanlış olabilir ya da istemci kapalı.";
                return;
            }

            Show(profile);

            // Rows first, artwork after: the list must be readable the instant the lookup
            // returns, not when somebody else's CDN finishes answering.
            await LoadPortraitsAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log.Error("Oyuncu aranamadı", exception);
            Clear();
            Status = "Arama başarısız.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Fills the panel from a profile already in hand.</summary>
    public void Show(PlayerProfile profile)
    {
        Ranks.Clear();
        Matches.Clear();

        foreach (var rank in profile.Ranks)
        {
            Ranks.Add(new RankLine(
                QueueName(rank.Queue),
                $"{Tier(rank.Tier)} {rank.Division} · {rank.LeaguePoints} LP",
                rank.WinRate is { } rate
                    ? $"{rank.Wins}G / {rank.Losses}M · %{rate * 100:F0}"
                    : $"{rank.Wins} galibiyet"));
        }

        foreach (var match in profile.Matches)
        {
            Matches.Add(new MatchLine(
                match.Won ? "G" : "M",
                match.Champion,
                $"{match.Kills}/{match.Deaths}/{match.Assists}",
                $"{match.CreepScore} CS",
                $"{(int)match.Duration.TotalMinutes}:{match.Duration.Seconds:00}",
                ModeName(match.Mode),
                match.PlayedAt.ToString("dd.MM HH:mm"),
                match.Won,
                Portrait(match.ChampionId),
                match.ChampionId));
        }

        Headline = $"{profile.RiotId} · seviye {profile.Level}";
        HasProfile = true;

        Status = profile.Ranks.Count == 0
            ? "Bu sezon dereceli kaydı yok."
            : string.Empty;
    }

    private void Clear()
    {
        Ranks.Clear();
        Matches.Clear();
        Headline = string.Empty;
        HasProfile = false;
    }

    private static string QueueName(string queue) => queue switch
    {
        "RANKED_SOLO_5x5" => "Tek/Çift",
        "RANKED_FLEX_SR" => "Esnek",
        _ => queue,
    };

    /// <summary>Riot sends tiers shouted; sentence case reads better in a list.</summary>
    private static string Tier(string tier) =>
        tier.Length <= 1 ? tier : tier[..1] + tier[1..].ToLowerInvariant();

    private static string ModeName(string mode) => mode switch
    {
        "CLASSIC" => "Rift",
        "ARAM" => "ARAM",
        "SWIFTPLAY" => "Hızlı",
        // Riot runs limited modes under names that mean nothing to a player, so an unknown mode is
        // shown as it came rather than guessed at.
        _ => mode,
    };
}
