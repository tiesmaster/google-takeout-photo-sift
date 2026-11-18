namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Input;

public enum Command
{
    Next,
    Previous,
    First,
    Last,
    RotateRight,
    RotateLeft,
    Rotate180,
    Delete,
    DeleteKeyHint,
    Undo,
    ToggleFullScreen,
    ToggleTiming,
    Escape,
}

public enum Group
{
    Navigate,
    Albums,
    Edit,
    View,
    Other,
}

/// <summary>Whether holding the key down repeats the command.</summary>
public enum Repeat
{
    Allow,
    Ignore,
}

public sealed record Binding(Command Command, IReadOnlyList<string> Keys, Group Group, string Description, Repeat Repeat);

/// <summary>
/// Every keyboard shortcut, in one place. The help overlay and the README table are generated from it.
/// </summary>
public static class Bindings
{
    public static readonly IReadOnlyList<Binding> All =
    [
        new(Command.Next, ["Right", "j", "Space", "PageDown"], Group.Navigate, "Next photo", Repeat.Allow),
        new(Command.Previous, ["Left", "k", "Backspace", "PageUp"], Group.Navigate, "Previous photo", Repeat.Allow),
        new(Command.First, ["Home"], Group.Navigate, "First photo", Repeat.Ignore),
        new(Command.Last, ["End", "G"], Group.Navigate, "Last photo", Repeat.Ignore),
        new(Command.RotateRight, ["r"], Group.Edit, "Rotate right 90°", Repeat.Ignore),
        new(Command.RotateLeft, ["l"], Group.Edit, "Rotate left 90°", Repeat.Ignore),
        new(Command.Rotate180, ["R"], Group.Edit, "Rotate 180°", Repeat.Ignore),
        new(Command.Delete, ["d"], Group.Edit, "Delete the photo and all its copies (to batches\\deleted)", Repeat.Ignore),
        // The Delete key itself does nothing but explain, so the habit doesn't silently fail.
        new(Command.DeleteKeyHint, ["Delete", "Shift+Delete"], Group.Edit, "Shows that delete is d", Repeat.Ignore),
        new(Command.Undo, ["z", "Ctrl+Z"], Group.Edit, "Undo the last delete", Repeat.Ignore),
        new(Command.ToggleFullScreen, ["f", "F11", "Enter"], Group.View, "Full screen", Repeat.Ignore),
        new(Command.ToggleTiming, ["F12"], Group.View, "Timing overlay", Repeat.Ignore),
        new(Command.Escape, ["Escape"], Group.Other, "Close overlay, leave full screen, else quit (after pending writes)", Repeat.Ignore),
    ];
}
