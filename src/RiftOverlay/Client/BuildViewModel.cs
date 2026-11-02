using System.Collections.ObjectModel;
using System.Windows.Media;
using RiftOverlay.Services;
using RiftOverlay.Services.Lcu;
using RiftOverlay.Services.OpGg;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Client;

/// <summary>
/// One line of a recommendation.
/// <para>
/// <see cref="Sample"/> is not optional. A live capture of the Challenger bracket returned a "best"
/// core build backed by six games — an 83% win rate that is pure noise — and the only thing standing
/// between that number and the reader is this field. Nothing here renders without it.
/// </para>
/// </summary>
/// <param name="Rate">Always the looked-up champion's own win rate, whichever list this came from.</param>
public sealed record CounterLine(string Champion, string Rate, string Sample, bool IsThin);

/// <summary>
/// One option for a single item slot. Slots four, five and six each have several, and their
/// samples collapse as the slot gets later — the capture behind the tests has three games behind
/// the most popular sixth item — so <see cref="Sample"/> and <see cref="IsThin"/> travel with every
/// one of them.
/// </summary>
public sealed class ItemOption(string name, string rate, string sample, bool isThin, bool? aboveBaseline)
{
    public string Name { get; } = name;

    public string Rate { get; } = rate;

    public string Sample { get; } = sample;

    public bool IsThin { get; } = isThin;

    /// <summary>
    /// Whether this beats the champion's own win rate — null when there is no honest answer.
    /// <para>
    /// Compared against the champion rather than against 50%, because 50% is a fact about the game
    /// and not about this champion: every row of a 47% champion would be red, which says nothing
    /// about which of them to build. Null on a thin sample, since colouring a three-game row green
    /// would be a verdict on noise.
    /// </para>
    /// </summary>
    public bool? AboveBaseline { get; } = aboveBaseline;

    public ObservableCollection<ImageSource> Icons { get; } = [];
}

public sealed class ItemSlot(string label)
{
    public string Label { get; } = label;

    public ObservableCollection<ItemOption> Options { get; } = [];
}

/// <summary>
/// One cell of the levelling grid. The level is carried rather than inferred from the index so the
/// view cannot silently renumber a shorter order — OP.GG returns fifteen steps, not eighteen.
/// </summary>
public sealed record SkillStep(int Level, string Ability, bool IsUltimate);

public sealed class BuildLine(
    string label, string value, string rate, string sample, bool isThin, bool? aboveBaseline)
{
    public string Label { get; } = label;

    public string Value { get; } = value;

    public string Rate { get; } = rate;

    public string Sample { get; } = sample;

    public bool IsThin { get; } = isThin;

    /// <summary>See <see cref="ItemOption.AboveBaseline"/> — the same rule, the same reasons.</summary>
    public bool? AboveBaseline { get; } = aboveBaseline;

    /// <summary>
    /// Artwork for the item rows, filled in as it arrives. An observable collection rather than a
    /// fixed list because the icons are fetched after the row is already on screen — the text must
    /// never wait on a download.
    /// </summary>
    public ObservableCollection<ImageSource> Icons { get; } = [];
}

/// <summary>
/// Turns an OP.GG champion analysis into rows the client can show, and writes the rune page back
/// into the League client on request.
/// <para>
/// Runes are writable because that path is on verified ground: the page shape was read from a live
/// client, and <see cref="RunePageService"/> only ever deletes pages it created itself. Item sets
/// are <em>not</em> written yet — the endpoint answers, but the exact payload has not been captured
/// from a running client, and inventing it is how this project has been burned before.
/// </para>
/// </summary>
public sealed class BuildViewModel : ObservableObject
{
    /// <summary>
    /// Below this many games a row is called out as thin rather than presented as advice. Two
    /// hundred is not a statistical threshold so much as an honesty one: it is comfortably past the
    /// range where a handful of games can swing a win rate by ten points.
    /// </summary>
    public const int ThinSample = 200;

    private readonly OpGgClient _opgg;
    private readonly SummonerSpellCatalog _spells;
    private readonly RunePageService? _runes;
    private readonly ItemSetService? _itemSets;
    private readonly IconCache? _icons;
    private readonly ILog _log;

    private ChampionAnalysis? _current;

    private string _champion = string.Empty;
    private string _position = "mid";
    private string _status = "Bir şampiyon yaz ve getir.";
    private bool _isBusy;
    private bool _hasBuild;
    private string _countersNote = string.Empty;
    private string _tierLabel = string.Empty;
    private string _positionRate = string.Empty;
    private string _damageLabel = string.Empty;
    private string _baselineNote = string.Empty;
    private double _baseline;

    public BuildViewModel(
        OpGgClient opgg,
        SummonerSpellCatalog spells,
        RunePageService? runes = null,
        ItemSetService? itemSets = null,
        IconCache? icons = null,
        ILog? log = null)
    {
        _opgg = opgg;
        _spells = spells;
        _runes = runes;
        _itemSets = itemSets;
        _icons = icons;
        _log = log ?? NullLog.Instance;
    }

    public ObservableCollection<BuildLine> Lines { get; } = [];

    /// <summary>Fourth, fifth and sixth item options, one group per slot.</summary>
    public ObservableCollection<ItemSlot> Slots { get; } = [];

    /// <summary>The recommended levelling order, one cell per level.</summary>
    public ObservableCollection<SkillStep> SkillOrder { get; } = [];

    /// <summary>Matchups this champion wins.</summary>
    public ObservableCollection<CounterLine> StrongAgainst { get; } = [];

    /// <summary>Matchups this champion loses.</summary>
    public ObservableCollection<CounterLine> WeakAgainst { get; } = [];

    public IReadOnlyList<string> Positions { get; } = ["top", "jungle", "mid", "adc", "support"];

    public string Champion
    {
        get => _champion;
        set => Set(ref _champion, value);
    }

    public string Position
    {
        get => _position;
        set => Set(ref _position, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public bool HasBuild
    {
        get => _hasBuild;
        private set
        {
            if (Set(ref _hasBuild, value))
            {
                Raise(nameof(CanApplyRunes));
                Raise(nameof(CanApplyItems));
            }
        }
    }

    /// <summary>
    /// Whether the rune page can be written. False without a build, and false when the row cannot
    /// make a valid page — the button disappears rather than writing something broken.
    /// </summary>
    public bool CanApplyRunes =>
        _runes is not null && _current is not null && OpGgRuneMapper.ToPreset(_current.Runes) is not null;

    /// <summary>
    /// Whether an item set can be written. Needs a champion id, which the analysis carries in its
    /// summary — without it the set could not be attached to the right champion.
    /// </summary>
    public bool CanApplyItems =>
        _itemSets is not null && _current is not null && _current.Summary.Id > 0 &&
        (_current.StarterItems.Ids.Count + _current.Boots.Ids.Count + _current.CoreItems.Ids.Count) > 0;

    /// <summary>
    /// Writes the recommended items as a set in the client.
    /// <para>
    /// The endpoint replaces the whole collection, so the service reads it first and edits it;
    /// nothing the user or another tool created is removed. The account this was built against
    /// held sixty-one sets from another tool — a naive write would have destroyed all of them.
    /// </para>
    /// </summary>
    public async Task ApplyItemsAsync()
    {
        if (_itemSets is null || _current is null || IsBusy || !CanApplyItems)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var set = ItemSetService.Build(
                _current.StarterItems.Ids, _current.Boots.Ids, _current.CoreItems.Ids);

            var written = await _itemSets
                .ApplyAsync(_current.Summary.Id, Champion.Trim(), set)
                .ConfigureAwait(true);

            Status = written
                ? $"Eşya seti yazıldı: {Champion.Trim()} — diğer setlerine dokunulmadı."
                : "Eşya seti yazılamadı — istemci kapalı olabilir.";
        }
        catch (Exception exception)
        {
            _log.Error("Eşya seti yazılamadı", exception);
            Status = "Eşya seti yazılamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The server's own explanation when a counter list came back empty. Shown instead of the list,
    /// because "no counters" and "not enough games to say" look identical otherwise.
    /// </summary>
    public string CountersNote
    {
        get => _countersNote;
        private set => Set(ref _countersNote, value);
    }

    public bool HasSlots => Slots.Count > 0;

    public bool HasSkillOrder => SkillOrder.Count > 0;

    /// <summary>
    /// OP.GG's own placement for this champion in this lane, shown as their label rather than
    /// restated as a fact — it is a ranking they compute, not a measurement.
    /// </summary>
    public string TierLabel
    {
        get => _tierLabel;
        private set => Set(ref _tierLabel, value);
    }

    public bool HasTier => _tierLabel.Length > 0;

    /// <summary>
    /// The win rate for this lane specifically. The blended average covers every position a
    /// champion is played in, so for a champion with real play in two lanes it belongs to neither.
    /// </summary>
    public string PositionRate
    {
        get => _positionRate;
        private set => Set(ref _positionRate, value);
    }

    public string DamageLabel
    {
        get => _damageLabel;
        private set => Set(ref _damageLabel, value);
    }

    /// <summary>
    /// Says what the colours are measured against. Without it a green rate reads as "good" in the
    /// absolute, which for a champion below 50% overall would be the wrong reading of every row.
    /// </summary>
    public string BaselineNote
    {
        get => _baselineNote;
        private set => Set(ref _baselineNote, value);
    }

    public bool HasStrong => StrongAgainst.Count > 0;

    public bool HasWeak => WeakAgainst.Count > 0;

    public bool HasCounters => HasStrong || HasWeak;

    /// <summary>
    /// What pressing apply would write. Shown next to the button so the page is never a surprise —
    /// applying deletes and recreates a rune page and there is no undo.
    /// </summary>
    public string RuneSummary => _current is null ? string.Empty : OpGgRuneMapper.Describe(_current.Runes);

    /// <summary>
    /// Writes the recommended page into the client. Only pages this tool created are removed to
    /// make room; a page the user built themselves is never touched.
    /// </summary>
    public async Task ApplyRunesAsync()
    {
        if (_runes is null || _current is null || IsBusy)
        {
            return;
        }

        var preset = OpGgRuneMapper.ToPreset(_current.Runes);

        if (preset is null)
        {
            Status = "Rün sayfası eksik geldi, yazılmadı.";
            return;
        }

        IsBusy = true;

        try
        {
            var applied = await _runes.ApplyAsync(Champion.Trim(), preset).ConfigureAwait(true);

            Status = applied
                ? $"Rün sayfası yazıldı: {OpGgRuneMapper.Describe(_current.Runes)}"
                : "Rün sayfası yazılamadı — istemci kapalı ya da sayfa limiti dolu olabilir.";
        }
        catch (Exception exception)
        {
            _log.Error("Rün uygulanamadı", exception);
            Status = "Rün sayfası yazılamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Loads the build for a champion picked elsewhere — champion select hands the locked pick
    /// straight in, so the user does not retype what the client already knows.
    /// </summary>
    public async Task LoadForAsync(string champion, string position)
    {
        if (string.IsNullOrWhiteSpace(champion))
        {
            return;
        }

        Champion = champion;

        if (!string.IsNullOrWhiteSpace(position) && Positions.Contains(position, StringComparer.OrdinalIgnoreCase))
        {
            Position = position.ToLowerInvariant();
        }

        await LoadAsync().ConfigureAwait(true);
    }

    public async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(Champion))
        {
            return;
        }

        IsBusy = true;
        Status = "OP.GG sorgulanıyor…";

        try
        {
            var build = await _opgg.GetBuildAsync(Champion.Trim(), Position).ConfigureAwait(true);

            if (build is null)
            {
                _current = null;
                Lines.Clear();
                HasBuild = false;

                // An unreachable OP.GG is a normal state, not a fault: there is no published quota
                // or uptime promise behind that endpoint.
                Status = "Build alınamadı — OP.GG'ye ulaşılamadı ya da bu şampiyon/koridor için veri yok.";
                return;
            }

            Show(build);

            var total = build.Summary.AverageStats.Play;
            Status = $"{Champion.Trim()} · {Position} · {total:N0} maç · %{build.Summary.AverageStats.WinRate * 100:F1} kazanma";
        }
        catch (Exception exception)
        {
            _log.Error("Build sorgusu başarısız", exception);
            Status = "Build alınamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Renders an analysis that is already in hand. Public so a champion-select hand-off can push
    /// one straight in, and so the rows can be exercised without going near the network.
    /// </summary>
    public void Show(ChampionAnalysis build)
    {
        _current = build;

        // Set before any row is built: every row's colour is measured against it.
        SetBaseline(build);

        Lines.Clear();

        Add("Başlangıç", Join(build.StarterItems.Names), build.StarterItems.Play, build.StarterItems.Win, build.StarterItems.Ids);
        Add("Çizme", Join(build.Boots.Names), build.Boots.Play, build.Boots.Win, build.Boots.Ids);
        Add("Çekirdek", Join(build.CoreItems.Names), build.CoreItems.Play, build.CoreItems.Win, build.CoreItems.Ids);

        // Spells arrive as bare ids where everything else carries names, so they are resolved here.
        Add(
            "Büyüler",
            string.Join(" + ", build.SummonerSpells.Ids.Select(_spells.NameOf)),
            build.SummonerSpells.Play,
            build.SummonerSpells.Win);

        Add("Rünler", Page(build.Runes.PrimaryPageName, build.Runes.PrimaryRuneNames), build.Runes.Play, build.Runes.Win);
        Add("İkincil", Page(build.Runes.SecondaryPageName, build.Runes.SecondaryRuneNames), build.Runes.Play, build.Runes.Win);

        Add("Yetenek", string.Join(" ", build.Skills.Order), build.Skills.Play, build.Skills.Win);
        Add("Maks sıra", string.Join(" > ", build.SkillMasteries.Ids), build.SkillMasteries.Play, build.SkillMasteries.Win);

        FillSlots(build);
        FillSkillOrder(build);
        FillHeadline(build);
        FillCounters(build);

        HasBuild = Lines.Count > 0;

        Raise(nameof(CanApplyRunes));
        Raise(nameof(CanApplyItems));
        Raise(nameof(RuneSummary));
        Raise(nameof(HasSlots));
        Raise(nameof(HasSkillOrder));
        Raise(nameof(HasTier));
    }

    /// <summary>
    /// Picks the champion's own win rate as the yardstick, preferring the lane it is being looked
    /// up in over the blend across every lane it is played in.
    /// </summary>
    private void SetBaseline(ChampionAnalysis build)
    {
        var lane = build.Summary.Positions.FirstOrDefault()?.Stats;

        _baseline = lane is { Play: > 0 } ? lane.WinRate : build.Summary.AverageStats.WinRate;

        BaselineNote = _baseline <= 0
            ? string.Empty
            : $"Renkler şampiyonun kendi oranına (%{_baseline * 100:F1}) göre. İnce örneklemler renklendirilmiyor.";
    }

    /// <summary>
    /// How far a rate has to sit from the champion's own before it counts as a difference.
    /// <para>
    /// Without it the first render coloured a row reading "%47,0" red against a baseline that also
    /// read "%47,0" — two numbers the reader sees as identical, one of them marked as worse. A
    /// point of win rate is the smallest gap worth a colour.
    /// </para>
    /// </summary>
    private const double MeaningfulGap = 0.01;

    /// <summary>
    /// Null on a thin sample, null without a baseline, and null inside the dead band. All three are
    /// "no verdict", which is a different thing from "not better".
    /// </summary>
    private bool? Compare(int play, int win)
    {
        if (play < ThinSample || play == 0 || _baseline <= 0)
        {
            return null;
        }

        var rate = (double)win / play;

        return Math.Abs(rate - _baseline) < MeaningfulGap ? null : rate > _baseline;
    }

    /// <summary>
    /// Fills the per-slot item options.
    /// <para>
    /// Only three slots are shown. The response also carries a <c>last_items</c> list whose samples
    /// run several times larger than the core build and whose entries overlap it only partly; what
    /// a slot in that list denotes is not something this code knows, and a slot label guessed wrong
    /// is worse than a slot left out.
    /// </para>
    /// </summary>
    private void FillSlots(ChampionAnalysis build)
    {
        Slots.Clear();

        Add("4. eşya", build.FourthItems);
        Add("5. eşya", build.FifthItems);
        Add("6. eşya", build.SixthItems);

        void Add(string label, IReadOnlyList<BuildRow> rows)
        {
            if (rows.Count == 0)
            {
                return;
            }

            var slot = new ItemSlot(label);

            foreach (var row in rows.Take(3))
            {
                var option = new ItemOption(
                    row.Names.Count > 0 ? string.Join(" / ", row.Names) : string.Join(" / ", row.Ids),
                    row.WinRate is { } rate ? $"%{rate * 100:F1}" : "—",
                    $"{row.Play:N0} maç",
                    row.Play < ThinSample,
                    Compare(row.Play, row.Win));

                slot.Options.Add(option);
                LoadIcons(option.Icons, row.Ids);
            }

            Slots.Add(slot);
        }
    }

    /// <summary>
    /// Turns the levelling order into one cell per level.
    /// <para>
    /// The level number is carried on each cell rather than left to the view's index, because the
    /// order is fifteen steps long and a champion is levelled eighteen times. Numbering by position
    /// happens to be right here and would quietly stop being right if the server ever returned the
    /// full order.
    /// </para>
    /// </summary>
    private void FillSkillOrder(ChampionAnalysis build)
    {
        SkillOrder.Clear();

        // The alternatives come sorted by games played, so the first one is the recommendation; the
        // plain order field is the fallback for a champion that has no alternatives listed.
        var order = build.SkillMasteries.Builds.Count > 0
            ? build.SkillMasteries.Builds[0].Order
            : build.Skills.Order;

        for (var index = 0; index < order.Count; index++)
        {
            var ability = order[index].ToUpperInvariant();
            SkillOrder.Add(new SkillStep(index + 1, ability, ability == "R"));
        }
    }

    /// <summary>
    /// The lane-specific headline. Falls back to nothing rather than to the blended average: an
    /// absent position block is a reason to show less, not a reason to show a number from a
    /// different question.
    /// </summary>
    private void FillHeadline(ChampionAnalysis build)
    {
        DamageLabel = build.DamageType switch
        {
            "AD" => "Fiziksel hasar",
            "AP" => "Sihirsel hasar",
            "BOTH" => "Karma hasar",
            _ => string.Empty,
        };

        var position = build.Summary.Positions.FirstOrDefault();

        if (position?.Stats is not { } stats)
        {
            TierLabel = string.Empty;
            PositionRate = string.Empty;
            return;
        }

        TierLabel = stats.TierData is { Tier: > 0 } tier
            ? $"OP.GG {tier.Tier}. kademe · #{tier.Rank}"
            : string.Empty;

        PositionRate = stats.Play == 0
            ? string.Empty
            : $"%{stats.WinRate * 100:F1} · {stats.Play:N0} maç · bu koridorda %{stats.RoleRate * 100:F0}";
    }

    /// <summary>
    /// Both counter lists, each showing <em>our</em> win rate rather than the opponent's.
    /// <para>
    /// The two lists report win rates from opposite sides — in <c>weak_counters</c> the plain
    /// <c>win_rate</c> belongs to the counter pick, not to the champion being looked up — so mixing
    /// them without care produces a table where half the numbers are inverted and nothing says so.
    /// <c>my_win_rate</c> is unambiguous in both, which is why it is the one used.
    /// </para>
    /// </summary>
    private void FillCounters(ChampionAnalysis build)
    {
        StrongAgainst.Clear();
        WeakAgainst.Clear();

        foreach (var counter in build.StrongCounters.Take(5))
        {
            StrongAgainst.Add(ToCounter(counter));
        }

        foreach (var counter in build.WeakCounters.Take(5))
        {
            WeakAgainst.Add(ToCounter(counter));
        }

        CountersNote = HasCounters ? string.Empty : build.CountersMeta?.Message ?? string.Empty;

        Raise(nameof(HasStrong));
        Raise(nameof(HasWeak));
        Raise(nameof(HasCounters));
    }

    /// <summary>
    /// A missing rate is shown as unknown, not as zero.
    /// <para>
    /// This is not hypothetical: the field was added to the request after the first capture, and
    /// the stale fixture rendered every matchup as a confident "%0". A schema change on the
    /// server would do the same thing in production, and a zero win rate in a matchup with enough
    /// games to be listed at all is close enough to impossible that treating it as missing costs
    /// nothing.
    /// </para>
    /// </summary>
    private static CounterLine ToCounter(CounterRow counter) => new(
        counter.ChampionName,
        counter.MyWinRate <= 0 ? "—" : $"%{counter.MyWinRate * 100:F0}",
        $"{counter.Play:N0} maç",
        counter.Play < ThinSample);

    private void Add(string label, string value, int play, int win, IReadOnlyList<int>? itemIds = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var line = new BuildLine(
            label,
            value,
            play == 0 ? "—" : $"%{100.0 * win / play:F1}",
            $"{play:N0} maç",
            play < ThinSample,
            Compare(play, win));

        Lines.Add(line);

        if (itemIds is not null)
        {
            LoadIcons(line.Icons, itemIds);
        }
    }

    /// <summary>
    /// Fetches artwork after the row is already visible.
    /// <para>
    /// Fire-and-forget, and sequential rather than parallel: the rows are short and the icons come
    /// off a disk cache after the first match, so a queue of six requests is cheaper than six
    /// simultaneous connections to somebody else's CDN.
    /// </para>
    /// </summary>
    private async void LoadIcons(ObservableCollection<ImageSource> target, IReadOnlyList<int> itemIds)
    {
        if (_icons is null)
        {
            return;
        }

        try
        {
            foreach (var id in itemIds)
            {
                var image = await _icons.GetItemIconAsync(id).ConfigureAwait(true);

                if (image is not null)
                {
                    target.Add(image);
                }
            }
        }
        catch (Exception exception)
        {
            // An async void method that throws takes the process down; artwork is never worth that.
            _log.Warn($"Build ikonları yüklenemedi: {exception.Message}");
        }
    }

    private static string Join(IReadOnlyList<string> names) => string.Join(" → ", names);

    /// <summary>
    /// Formats a rune page, or nothing at all when there is no page.
    /// <para>
    /// The separator is why this is not inline: building "{page}: {runes}" from empty parts yields
    /// ": ", which is not whitespace and therefore sails past the empty check in
    /// <see cref="Add"/> — producing a row that reads ": " with "0 maç" beside it. Tests caught it;
    /// a champion with no rune data would have shown two of them.
    /// </para>
    /// </summary>
    private static string Page(string name, IReadOnlyList<string> runes)
    {
        if (string.IsNullOrWhiteSpace(name) && runes.Count == 0)
        {
            return string.Empty;
        }

        return runes.Count == 0 ? name : $"{name}: {string.Join(", ", runes)}";
    }
}
