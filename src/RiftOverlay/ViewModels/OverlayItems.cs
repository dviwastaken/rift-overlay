using System.Windows;
using System.Windows.Media;
using RiftOverlay.Services;

namespace RiftOverlay.ViewModels;

public static class Palette
{
    public static readonly SolidColorBrush Dragon = Freeze("#FFE2603A");
    public static readonly SolidColorBrush Elder = Freeze("#FF7FD8E8");
    public static readonly SolidColorBrush Baron = Freeze("#FFA77BE8");
    public static readonly SolidColorBrush Herald = Freeze("#FF6FA8FF");
    public static readonly SolidColorBrush Ready = Freeze("#FF5BD98A");
    public static readonly SolidColorBrush Enemy = Freeze("#FFE8685F");
    public static readonly SolidColorBrush Spell = Freeze("#FFE8C15F");
    public static readonly SolidColorBrush Inhibitor = Freeze("#FFE86FB0");
    public static readonly SolidColorBrush Voidgrubs = Freeze("#FF6FD3B0");
    public static readonly SolidColorBrush Gold = Freeze("#FFF2C14E");
    public static readonly SolidColorBrush Muted = Freeze("#FF7A8496");

    /// <summary>
    /// A countdown about to expire. Warm amber rather than the "ready" green: green already means
    /// "it is up now", and reusing it would say the objective has spawned when it has not.
    /// </summary>
    public static readonly SolidColorBrush Imminent = Freeze("#FFF2A03C");

    public static SolidColorBrush For(ObjectiveKind kind) => kind switch
    {
        ObjectiveKind.Dragon => Dragon,
        ObjectiveKind.Elder or ObjectiveKind.ElderBuff => Elder,
        ObjectiveKind.Baron or ObjectiveKind.BaronBuff => Baron,
        ObjectiveKind.Inhibitor => Inhibitor,
        ObjectiveKind.Voidgrubs => Voidgrubs,
        // The wave timer is constant background information, so it never competes for attention.
        ObjectiveKind.Wave => Muted,
        _ => Herald,
    };

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <param name="Record">"7/2/9" — kills, deaths and assists as the scoreboard writes them.</param>
public sealed record HistoryItem(
    string Champion,
    string Record,
    string Duration,
    string Result,
    SolidColorBrush ResultBrush);

/// <param name="WinRate">Blank when no result was ever recorded for this champion.</param>
public sealed record ChampionLine(string Champion, string Played, string WinRate, string CreepScoreAtTen);

public sealed class ObjectiveItem : ObservableObject
{
    /// <summary>
    /// How close a countdown has to get before the row is called out. Thirty seconds is roughly
    /// the window in which a team can still walk to the pit and contest — earlier than that the
    /// warning is noise, later and it arrives after the decision was already made.
    /// </summary>
    public const double ImminentSeconds = 30;

    private string _remaining = "--:--";
    private string _detail = string.Empty;
    private bool _isReady;
    private bool _isImminent;

    public ObjectiveItem(ObjectiveKind kind, string name)
    {
        Kind = kind;
        Name = name;
        Accent = Palette.For(kind);
    }

    public ObjectiveKind Kind { get; }

    public string Name { get; }

    public SolidColorBrush Accent { get; }

    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    public string Remaining
    {
        get => _remaining;
        set => Set(ref _remaining, value);
    }

    /// <summary>True once the objective is alive in the pit rather than counting down.</summary>
    public bool IsReady
    {
        get => _isReady;
        set
        {
            if (Set(ref _isReady, value))
            {
                Raise(nameof(StatusBrush));
            }
        }
    }

    /// <summary>
    /// Set while the objective is about to spawn but has not yet. Deliberately false once
    /// <see cref="IsReady"/> is true: an objective standing in the pit is a different situation
    /// from one that is thirty seconds out, and showing both the same way would flatten exactly
    /// the distinction the row exists to make.
    /// </summary>
    public bool IsImminent
    {
        get => _isImminent;
        set
        {
            if (Set(ref _isImminent, value))
            {
                Raise(nameof(StatusBrush));
                Raise(nameof(RowWeight));
            }
        }
    }

    public SolidColorBrush StatusBrush =>
        _isReady ? Palette.Ready : _isImminent ? Palette.Imminent : Accent;

    /// <summary>
    /// Weight carries the warning alongside colour, so the row still reads as urgent for a
    /// colour-blind player and against the bright, busy background of a teamfight.
    /// </summary>
    public FontWeight RowWeight => _isReady || _isImminent ? FontWeights.Bold : FontWeights.Normal;
}

/// <summary>One tracked summoner spell slot on an enemy row.</summary>
public sealed class SpellItem : ObservableObject
{
    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Flash"] = "Fl",
        ["Teleport"] = "TP",
        ["Ignite"] = "Ig",
        ["Barrier"] = "Ba",
        ["Heal"] = "He",
        ["Exhaust"] = "Ex",
        ["Ghost"] = "Gh",
        ["Cleanse"] = "Cl",
        ["Smite"] = "Sm",
    };

    private string _name = string.Empty;
    private string _remaining = string.Empty;
    private bool _isReady = true;

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value))
            {
                Raise(nameof(ShortName));
            }
        }
    }

    public string ShortName => Abbreviations.TryGetValue(_name, out var value)
        ? value
        : _name.Length >= 2 ? _name[..2] : _name;

    /// <summary>Countdown text, or empty while the spell is up.</summary>
    public string Remaining
    {
        get => _remaining;
        set => Set(ref _remaining, value);
    }

    public bool IsReady
    {
        get => _isReady;
        set
        {
            if (Set(ref _isReady, value))
            {
                Raise(nameof(Foreground));
            }
        }
    }

    public SolidColorBrush Foreground => _isReady ? Palette.Muted : Palette.Spell;
}

/// <summary>The item the player is building toward, and what is still missing to buy it.</summary>
public sealed class BuildTargetItem : ObservableObject
{
    private bool _hasTarget;
    private ImageSource? _icon;
    private string _itemName = string.Empty;
    private string _remaining = string.Empty;
    private string _detail = string.Empty;
    private double _completion;
    private bool _isAffordable;

    public bool HasTarget
    {
        get => _hasTarget;
        set => Set(ref _hasTarget, value);
    }

    /// <summary>Null until the artwork arrives, and stays null if it never does.</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    public string ItemName
    {
        get => _itemName;
        set => Set(ref _itemName, value);
    }

    /// <summary>Gold still to earn, or the affordable state once it reaches zero.</summary>
    public string Remaining
    {
        get => _remaining;
        set => Set(ref _remaining, value);
    }

    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    /// <summary>0–1, driving the meter under the number.</summary>
    public double Completion
    {
        get => _completion;
        set => Set(ref _completion, value);
    }

    public bool IsAffordable
    {
        get => _isAffordable;
        set
        {
            if (Set(ref _isAffordable, value))
            {
                Raise(nameof(Accent));
            }
        }
    }

    public SolidColorBrush Accent => _isAffordable ? Palette.Ready : Palette.Gold;
}

/// <summary>One completed enemy item, as shown in the power-spike feed.</summary>
public sealed class EnemyItemItem : ObservableObject
{
    private bool _isLatest;
    private ImageSource? _icon;

    public EnemyItemItem(string clock, string champion, int itemId, string itemName)
    {
        Clock = clock;
        Champion = champion;
        ItemId = itemId;
        ItemName = itemName;
    }

    public int ItemId { get; }

    /// <summary>Null until the artwork arrives, and stays null if it never does.</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    public string Clock { get; }

    public string Champion { get; }

    public string ItemName { get; }

    public bool IsLatest
    {
        get => _isLatest;
        set
        {
            if (Set(ref _isLatest, value))
            {
                Raise(nameof(Foreground));
            }
        }
    }

    public SolidColorBrush Foreground => _isLatest ? Palette.Enemy : Palette.Muted;
}

/// <summary>One row of a picker in the settings card. The first entry is always "automatic".</summary>
public abstract class PickerItem : ObservableObject
{
    private bool _isSelected;

    protected PickerItem(string name) => Name = name;

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                Raise(nameof(Foreground));
            }
        }
    }

    public SolidColorBrush Foreground => _isSelected ? Palette.Gold : Palette.Muted;
}

public sealed class BuildCandidateItem : PickerItem
{
    public BuildCandidateItem(int? itemId, string name)
        : base(name) => ItemId = itemId;

    /// <summary>Null is the "infer it from my inventory" entry.</summary>
    public int? ItemId { get; }
}

public sealed class LaneCandidateItem : PickerItem
{
    public LaneCandidateItem(string? player, string name)
        : base(name) => Player = player;

    /// <summary>Null is the "match on position" entry.</summary>
    public string? Player { get; }
}

/// <summary>One player's pick in champion select.</summary>
public sealed class ChampSelectPickItem : ObservableObject
{
    public ChampSelectPickItem(string champion, string position, bool isLocked, bool isSelf)
    {
        Champion = champion;
        Position = string.IsNullOrWhiteSpace(position) ? string.Empty : position.ToLowerInvariant();
        IsLocked = isLocked;
        IsSelf = isSelf;
    }

    public string Champion { get; }

    public string Position { get; }

    public bool IsLocked { get; }

    public bool IsSelf { get; }

    /// <summary>A hovered champion can still change, so it reads dimmer than a locked one.</summary>
    public SolidColorBrush Foreground => IsSelf
        ? Palette.Ready
        : IsLocked ? Palette.Enemy : Palette.Muted;
}

/// <summary>One editable key binding in the settings card.</summary>
public sealed class HotkeyItem : ObservableObject
{
    private string _display = string.Empty;
    private bool _isCapturing;

    public HotkeyItem(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }

    public string Label { get; }

    public string Display
    {
        get => _display;
        set => Set(ref _display, value);
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            if (Set(ref _isCapturing, value))
            {
                Raise(nameof(Text));
                Raise(nameof(Foreground));
            }
        }
    }

    public string Text => _isCapturing ? "tuşa bas… (Esc iptal)" : $"{Label}   {_display}";

    public SolidColorBrush Foreground => _isCapturing ? Palette.Gold : Palette.Muted;
}

/// <summary>The player against their lane opponent — the numbers that decide whether to trade.</summary>
public sealed class LaneItem : ObservableObject
{
    private bool _hasOpponent;
    private string _opponent = string.Empty;
    private string _detail = string.Empty;
    private string _selfCreepScore = "0";
    private string _opponentCreepScore = "0";
    private string _creepScoreDifference = "0";
    private string _selfLevel = "1";
    private string _opponentLevel = "1";
    private string _levelDifference = "0";
    private int _creepScoreLead;
    private int _levelLead;

    public bool HasOpponent
    {
        get => _hasOpponent;
        set => Set(ref _hasOpponent, value);
    }

    public string Opponent
    {
        get => _opponent;
        set => Set(ref _opponent, value);
    }

    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    public string SelfCreepScore
    {
        get => _selfCreepScore;
        set => Set(ref _selfCreepScore, value);
    }

    public string OpponentCreepScore
    {
        get => _opponentCreepScore;
        set => Set(ref _opponentCreepScore, value);
    }

    public string CreepScoreDifference
    {
        get => _creepScoreDifference;
        set => Set(ref _creepScoreDifference, value);
    }

    public string SelfLevel
    {
        get => _selfLevel;
        set => Set(ref _selfLevel, value);
    }

    public string OpponentLevel
    {
        get => _opponentLevel;
        set => Set(ref _opponentLevel, value);
    }

    public string LevelDifference
    {
        get => _levelDifference;
        set => Set(ref _levelDifference, value);
    }

    public int CreepScoreLead
    {
        get => _creepScoreLead;
        set
        {
            if (Set(ref _creepScoreLead, value))
            {
                Raise(nameof(CreepScoreBrush));
            }
        }
    }

    public int LevelLead
    {
        get => _levelLead;
        set
        {
            if (Set(ref _levelLead, value))
            {
                Raise(nameof(LevelBrush));
            }
        }
    }

    public SolidColorBrush CreepScoreBrush => LeadBrush(_creepScoreLead);

    public SolidColorBrush LevelBrush => LeadBrush(_levelLead);

    /// <summary>Green means ahead and red behind, matching how the rest of the overlay reads.</summary>
    private static SolidColorBrush LeadBrush(int lead) =>
        lead > 0 ? Palette.Ready : lead < 0 ? Palette.Enemy : Palette.Muted;
}

public sealed class RespawnItem : ObservableObject
{
    private string _remaining = string.Empty;
    private bool _isDead;
    private string _kda = "0/0/0";
    private int _level = 1;

    public RespawnItem(string champion, string player)
    {
        Champion = champion;
        Player = player;
    }

    public SpellItem SpellOne { get; } = new();

    public SpellItem SpellTwo { get; } = new();

    public string Champion { get; }

    public string Player { get; }

    public string Remaining
    {
        get => _remaining;
        set => Set(ref _remaining, value);
    }

    public bool IsDead
    {
        get => _isDead;
        set
        {
            if (Set(ref _isDead, value))
            {
                Raise(nameof(StatusBrush));
            }
        }
    }

    public string Kda
    {
        get => _kda;
        set => Set(ref _kda, value);
    }

    public int Level
    {
        get => _level;
        set
        {
            if (Set(ref _level, value))
            {
                Raise(nameof(UltRank));
                Raise(nameof(UltLabel));
                Raise(nameof(UltBrush));
            }
        }
    }

    /// <summary>
    /// Ultimate rank, 0 through 3, derived from level.
    /// <para>
    /// This is one of the few things about an enemy that can be stated as fact rather than
    /// estimated: the API reports their level, and levels 6, 11 and 16 unlock the ranks. No
    /// guessing, no manual marking, no unreadable runes involved — which is exactly why the row
    /// shows availability and not a cooldown. A cooldown would have to assume the ability haste
    /// hiding in the enemy's minor runes, and getting that wrong makes the overlay claim an
    /// ultimate is still down when it is already back up.
    /// </para>
    /// </summary>
    public int UltRank => _level switch
    {
        >= 16 => 3,
        >= 11 => 2,
        >= 6 => 1,
        _ => 0,
    };

    public string UltLabel => UltRank == 0 ? "R" : $"R{UltRank}";

    /// <summary>Lit once the ultimate exists at all; the rank is the finer detail.</summary>
    public SolidColorBrush UltBrush => UltRank == 0 ? Palette.Muted : Palette.Spell;

    public SolidColorBrush StatusBrush => _isDead ? Palette.Enemy : Palette.Muted;
}
