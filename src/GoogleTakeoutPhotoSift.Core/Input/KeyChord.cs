namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Input;

/// <summary>
/// One key press, independent of the UI toolkit. Printable characters are the character itself, with
/// its case (<c>j</c>, <c>J</c>, <c>?</c>), so they follow the keyboard layout and never carry Shift.
/// Other keys use names (<c>Right</c>, <c>F11</c>, <c>Space</c>) and may carry modifiers.
/// </summary>
public readonly record struct KeyChord(string Key, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    /// <summary>Parses <c>j</c>, <c>J</c>, <c>Right</c>, <c>Shift+Right</c>, <c>Ctrl+Z</c>.</summary>
    public static KeyChord Parse(string text)
    {
        var parts = text.Split('+');
        // "+" itself is a key, and "Ctrl++" ends in an empty part.
        var key = parts[^1].Length == 0 ? "+" : parts[^1];
        var modifiers = parts[..^1].Where(p => p.Length > 0).ToList();
        var chord = new KeyChord(key);
        foreach (var modifier in modifiers)
        {
            chord = modifier switch
            {
                "Ctrl" => chord with { Ctrl = true },
                "Alt" => chord with { Alt = true },
                "Shift" => chord with { Shift = true },
                _ => throw new FormatException($"Unknown modifier '{modifier}' in '{text}'"),
            };
        }
        return chord;
    }

    public override string ToString() =>
        (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + Key;
}
