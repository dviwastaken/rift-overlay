using System.Collections.ObjectModel;
using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Client;

/// <param name="Record">"7/2/9" as the scoreboard writes it.</param>
public sealed record StatsMatchLine(
    string Result, string Champion, string Record, string CreepScore, string Duration, string When, bool Won);

/// <param name="WinRate">Blank when no match on this champion ever recorded a result.</param>
public sealed record ChampionStatLine(
    string Champion,
    string Played,
    string WinRate,
    string Kda,
    string CreepScoreAtTen,
    /// <summary>
    /// Null when there is no verdict: no decided match, or too few to mean anything. Fifty per cent
    /// is the honest yardstick here in a way it is not on a build screen — this is your own record,
    /// not a champion's average across everyone who plays it.
    /// </summary>
    bool? AboveEven);

/// <param name="Value">The measured figure, already formatted.</param>
/// <param name="Detail">What the figure is drawn from, so a thin sample is visible.</param>
public sealed record HeadlineStat(string Label, string Value, string Detail);

/// <summary>
/// Personal statistics, read from the matches this tool recorded itself.
/// <para>
/// This is the half no site can provide. OP.GG can say a build is generally good; only a local
/// record can say how it has actually gone for this player. It reads the same database the overlay
/// writes during a game — see <see cref="MatchDatabase"/>.
/// </para>
/// <para>
/// The rules from the recorder carry through unchanged, because breaking them here would undo them:
/// a match whose result was never seen counts as played but neither won nor lost, and CS at ten
/// minutes only comes from matches that actually reached ten minutes.
/// </para>
/// </summary>
public sealed class StatsViewModel : ObservableObject
{
    private readonly MatchDatabase _database;
    private readonly ILog _log;

    private string _status = string.Empty;
    private bool _hasData;

    public StatsViewModel(MatchDatabase database, ILog? log = null)
    {
        _database = database;
        _log = log ?? NullLog.Instance;
    }

    public ObservableCollection<HeadlineStat> Headlines { get; } = [];

    public ObservableCollection<StatsMatchLine> Matches { get; } = [];

    public ObservableCollection<ChampionStatLine> Champions { get; } = [];

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool HasData
    {
        get => _hasData;
        private set => Set(ref _hasData, value);
    }

    /// <summary>
    /// Rebuilds from disk. Called when the section is opened rather than on a timer: nothing here
    /// changes except when a match ends, and the overlay is what ends matches.
    /// </summary>
    public void Refresh()
    {
        try
        {
            Load(_database.GetRecentMatches(50), _database.GetChampionStats());
        }
        catch (Exception exception)
        {
            _log.Error("İstatistikler okunamadı", exception);
            Status = "İstatistikler okunamadı.";
            HasData = false;
        }
    }

    /// <summary>Fills the panel from records already in hand — used directly by tests.</summary>
    public void Load(IReadOnlyList<MatchRecord> matches, IReadOnlyList<ChampionStat> champions)
    {
        Headlines.Clear();
        Matches.Clear();
        Champions.Clear();

        var finished = matches.Where(m => m.EndedAtUtc is not null).ToList();

        if (finished.Count == 0)
        {
            HasData = false;
            Status = "Henüz kayıtlı maç yok. Bir maç oynadığında burası dolar.";
            return;
        }

        FillHeadlines(finished);

        foreach (var match in finished.Take(20))
        {
            Matches.Add(new StatsMatchLine(
                ResultLabel(match.Result),
                match.Champion,
                $"{match.Kills}/{match.Deaths}/{match.Assists}",
                $"{match.CreepScore} CS",
                Duration(match.DurationSeconds),
                match.StartedAtUtc.ToLocalTime().ToString("dd.MM HH:mm"),
                match.Result == "Win"));
        }

        foreach (var champion in champions.Take(8))
        {
            Champions.Add(new ChampionStatLine(
                champion.Champion,
                $"{champion.Matches} maç",
                champion.DecidedMatches == 0
                    ? "—"
                    : $"%{100.0 * champion.Wins / champion.DecidedMatches:F0}",
                $"{champion.AverageKills:F1}/{champion.AverageDeaths:F1}/{champion.AverageAssists:F1}",
                champion.AverageCreepScoreAtTen is { } cs ? $"{cs:F0}" : "—",
                Verdict(champion)));
        }

        HasData = true;
        Status = string.Empty;
    }

    private void FillHeadlines(IReadOnlyList<MatchRecord> matches)
    {
        var decided = matches.Where(m => m.Result is "Win" or "Lose").ToList();
        var wins = decided.Count(m => m.Result == "Win");

        Headlines.Add(new HeadlineStat(
            "Kayıtlı maç",
            matches.Count.ToString(),
            decided.Count == matches.Count
                ? "hepsinin sonucu biliniyor"
                : $"{matches.Count - decided.Count} tanesinin sonucu görülmedi"));

        // A win rate over zero decided matches would read as 0%, which is a claim rather than an
        // absence — the same rule the history panel and the build rows follow.
        Headlines.Add(new HeadlineStat(
            "Kazanma",
            decided.Count == 0 ? "—" : $"%{100.0 * wins / decided.Count:F0}",
            decided.Count == 0 ? "sonucu bilinen maç yok" : $"{wins}G / {decided.Count - wins}M"));

        var kills = matches.Sum(m => m.Kills);
        var deaths = matches.Sum(m => m.Deaths);
        var assists = matches.Sum(m => m.Assists);

        Headlines.Add(new HeadlineStat(
            "Ortalama KDA",
            deaths == 0 ? "—" : $"{(double)(kills + assists) / deaths:F2}",
            $"{kills}/{deaths}/{assists} toplam"));

        var vision = matches.Average(m => m.WardScore);

        Headlines.Add(new HeadlineStat(
            "Görüş puanı",
            $"{vision:F1}",
            "maç başına ortalama"));
    }

    /// <summary>
    /// How a champion's record reads against an even split, or no answer at all.
    /// <para>
    /// Four decided games is the floor. Below it a single result swings the number by a quarter,
    /// and colouring that is a verdict on a coin toss — the same reason the build screen refuses to
    /// colour a thin sample. An exact fifty per cent is left uncoloured too: it is neither.
    /// </para>
    /// </summary>
    private static bool? Verdict(ChampionStat champion)
    {
        const int Floor = 4;

        if (champion.DecidedMatches < Floor)
        {
            return null;
        }

        var wins = 2 * champion.Wins;

        return wins == champion.DecidedMatches ? null : wins > champion.DecidedMatches;
    }

    private static string ResultLabel(string? result) => result switch
    {
        "Win" => "G",
        "Lose" => "M",
        // A real API value: a remake, or a game that ended without a winner.
        "Neither" => "B",
        _ => "—",
    };

    private static string Duration(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);

        return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }
}
