namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Input;

public enum Command
{
    Next,
    Previous,
    First,
    Last,
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
        new(Command.ToggleFullScreen, ["f", "F11", "Enter"], Group.View, "Full screen", Repeat.Ignore),
        new(Command.ToggleTiming, ["F12"], Group.View, "Timing overlay", Repeat.Ignore),
        new(Command.Escape, ["Escape"], Group.Other, "Close overlay, leave full screen, else quit (after pending writes)", Repeat.Ignore),
    ];
}
