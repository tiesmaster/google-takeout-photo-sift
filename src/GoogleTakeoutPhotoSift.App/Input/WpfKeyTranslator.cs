using System.Windows.Input;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Input;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Input;

/// <summary>
/// Turns WPF key events into <see cref="KeyChord"/>s. Named keys (arrows, F-keys, Space…) and
/// Ctrl/Alt chords come from KeyDown; printable characters come from TextInput, so they follow the
/// keyboard layout. TextInput has no repeat flag, so the one from the preceding KeyDown is kept.
/// </summary>
public sealed class WpfKeyTranslator
{
    private static readonly Dictionary<Key, string> Named = new()
    {
        [Key.Left] = "Left", [Key.Right] = "Right", [Key.Up] = "Up", [Key.Down] = "Down",
        [Key.Home] = "Home", [Key.End] = "End", [Key.PageUp] = "PageUp", [Key.PageDown] = "PageDown",
        [Key.Space] = "Space", [Key.Back] = "Backspace", [Key.Enter] = "Enter", [Key.Escape] = "Escape",
        [Key.Delete] = "Delete", [Key.Insert] = "Insert", [Key.Tab] = "Tab",
        [Key.F1] = "F1", [Key.F2] = "F2", [Key.F3] = "F3", [Key.F4] = "F4", [Key.F5] = "F5", [Key.F6] = "F6",
        [Key.F7] = "F7", [Key.F8] = "F8", [Key.F9] = "F9", [Key.F10] = "F10", [Key.F11] = "F11", [Key.F12] = "F12",
    };

    private bool _lastKeyDownWasRepeat;

    /// <summary>The chord for a KeyDown, or null when the key arrives as text instead.</summary>
    public KeyChord? FromKeyDown(KeyEventArgs e)
    {
        _lastKeyDownWasRepeat = e.IsRepeat;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var alt = modifiers.HasFlag(ModifierKeys.Alt);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        if (Named.TryGetValue(key, out var name))
            return new KeyChord(name, ctrl, alt, shift);
        if ((ctrl || alt) && key is >= Key.A and <= Key.Z)
            return new KeyChord(key.ToString(), ctrl, alt, shift);
        if ((ctrl || alt) && key is >= Key.D0 and <= Key.D9)
            return new KeyChord(((char)('0' + (key - Key.D0))).ToString(), ctrl, alt, shift);
        return null;
    }

    /// <summary>The chord for a TextInput, or null for control characters and keys handled as named keys.</summary>
    public (KeyChord Chord, bool IsRepeat)? FromTextInput(TextCompositionEventArgs e)
    {
        var text = e.Text;
        if (string.IsNullOrEmpty(text) || text == " " || char.IsControl(text[0]))
            return null;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return null;
        return (new KeyChord(text), _lastKeyDownWasRepeat);
    }

    public bool LastKeyDownWasRepeat => _lastKeyDownWasRepeat;
}
