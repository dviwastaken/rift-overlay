using System.Text;

namespace RiftOverlay.Services;

/// <summary>Matches the Win32 MOD_ values so a binding converts to RegisterHotKey directly.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>
/// A key combination, stored in settings as the same text the UI shows ("Ctrl+Alt+O") so the file
/// stays hand-editable.
/// <para>
/// Some bindings are modifiers only: the five Flash keys are a modifier applied to 1–5, so the user
/// configures the combination once instead of five times, and a conflict is almost always with the
/// combination rather than a single digit.
/// </para>
/// </summary>
public sealed record HotkeyBinding(HotkeyModifiers Modifiers, uint VirtualKey, string KeyName)
{
    public bool IsModifiersOnly => VirtualKey == 0;

    public string Display
    {
        get
        {
            var text = new StringBuilder();

            if (Modifiers.HasFlag(HotkeyModifiers.Control))
            {
                text.Append("Ctrl+");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            {
                text.Append("Alt+");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            {
                text.Append("Shift+");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Windows))
            {
                text.Append("Win+");
            }

            return IsModifiersOnly ? text.ToString().TrimEnd('+') : text + KeyName;
        }
    }

    /// <returns>Null when the text names no modifier, or a key this cannot map to a virtual key.</returns>
    public static HotkeyBinding? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        string? keyName = null;

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "WIN" or "WINDOWS":
                    modifiers |= HotkeyModifiers.Windows;
                    break;
                default:
                    keyName = part.ToUpperInvariant();
                    break;
            }
        }

        // A hotkey with no modifier would swallow a bare keypress from the game.
        if (modifiers == HotkeyModifiers.None)
        {
            return null;
        }

        if (keyName is null)
        {
            return new HotkeyBinding(modifiers, 0, string.Empty);
        }

        var virtualKey = ToVirtualKey(keyName);

        return virtualKey == 0 ? null : new HotkeyBinding(modifiers, virtualKey, keyName);
    }

    /// <summary>Letters, digits and function keys — everything a person would sensibly bind here.</summary>
    public static uint ToVirtualKey(string keyName)
    {
        if (keyName.Length == 1)
        {
            var c = char.ToUpperInvariant(keyName[0]);

            if (c is >= 'A' and <= 'Z')
            {
                return c;
            }

            if (c is >= '0' and <= '9')
            {
                return c;
            }
        }

        if (keyName.Length is 2 or 3 && keyName[0] is 'F' or 'f' &&
            int.TryParse(keyName[1..], out var number) && number is >= 1 and <= 12)
        {
            return (uint)(0x70 + number - 1);
        }

        return 0;
    }
}

/// <summary>
/// Configurable bindings and their defaults. Ids reach settings.json, so renaming one silently
/// resets that binding.
/// </summary>
public static class HotkeyIds
{
    public const string Interactive = "interactive";
    public const string Visible = "visible";
    public const string FlashModifier = "flash";
    public const string SpellModifier = "spell";

    public static IReadOnlyDictionary<string, string> Defaults { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Interactive] = "Ctrl+Alt+O",
            [Visible] = "Ctrl+Alt+H",
            [FlashModifier] = "Ctrl+Alt",
            [SpellModifier] = "Ctrl+Alt+Shift",
        };

    public static IReadOnlyDictionary<string, string> Labels { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Interactive] = "Etkileşim modu",
            [Visible] = "Gizle / göster",
            [FlashModifier] = "Flash (+1..5)",
            [SpellModifier] = "Diğer spell (+1..5)",
        };

    public static bool IsModifiersOnly(string id) => id is FlashModifier or SpellModifier;

    public static string DefaultFor(string id) => Defaults.TryGetValue(id, out var text) ? text : string.Empty;
}
