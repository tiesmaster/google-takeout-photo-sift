namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Input;

public enum Command
{
    Next,
    Previous,
    First,
    Last,
    NextAlbum,
    PreviousAlbum,
    AlbumEnd,
    LoosePhotos,
    RotateRight,
    RotateLeft,
    Rotate180,
    Delete,
    DeleteKeyHint,
    Undo,
    ToggleStrip,
    StripSize,
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

/// <param name="Keys">Each a key (<c>j</c>, <c>Shift+Right</c>) or a two-key sequence (<c>g a</c>).</param>
/// <param name="Short">A few words for the pending-key hint (<c>g …  a albums</c>); defaults to the description.</param>
public sealed record Binding(Command Command, IReadOnlyList<string> Keys, Group Group, string Description, Repeat Repeat,
    string? Short = null);

/// <summary>
/// Every keyboard shortcut, in one place. The help overlay and the README table are generated from it.
/// </summary>
public static class Bindings
{
    public static readonly IReadOnlyList<Binding> All =
    [
        new(Command.Next, ["Right", "j", "Space", "PageDown"], Group.Navigate, "Next photo", Repeat.Allow),
        new(Command.Previous, ["Left", "k", "Backspace", "PageUp"], Group.Navigate, "Previous photo", Repeat.Allow),
        new(Command.First, ["Home", "g g"], Group.Navigate, "First photo of the batch", Repeat.Ignore, "first"),
        new(Command.Last, ["End", "G"], Group.Navigate, "Last photo of the batch", Repeat.Ignore),
        new(Command.NextAlbum, ["Shift+Right", "J", "]"], Group.Albums,
            "First photo of the next album (the loose photos count as the last album)", Repeat.Allow),
        new(Command.PreviousAlbum, ["Shift+Left", "K", "["], Group.Albums,
            "Start of this album; at its start, the previous album", Repeat.Allow),
        new(Command.AlbumEnd, ["g e"], Group.Albums, "Last photo of this album", Repeat.Ignore, "album end"),
        new(Command.LoosePhotos, ["g l"], Group.Albums, "First loose photo (in no album)", Repeat.Ignore, "loose"),
        new(Command.RotateRight, ["r"], Group.Edit, "Rotate right 90°", Repeat.Ignore),
        new(Command.RotateLeft, ["l"], Group.Edit, "Rotate left 90°", Repeat.Ignore),
        new(Command.Rotate180, ["R"], Group.Edit, "Rotate 180°", Repeat.Ignore),
        new(Command.Delete, ["d"], Group.Edit, "Delete the photo and all its copies (to batches\\deleted)", Repeat.Ignore),
        // The Delete key itself does nothing but explain, so the habit doesn't silently fail.
        new(Command.DeleteKeyHint, ["Delete", "Shift+Delete"], Group.Edit, "Shows that delete is d", Repeat.Ignore),
        new(Command.Undo, ["z", "Ctrl+Z"], Group.Edit, "Undo the last delete", Repeat.Ignore),
        new(Command.ToggleStrip, ["t"], Group.View, "Show or hide the thumbnail strip", Repeat.Ignore),
        new(Command.StripSize, ["T"], Group.View, "More or fewer thumbnails: 5, 8 or 3 each side", Repeat.Ignore),
        new(Command.ToggleFullScreen, ["f", "F11", "Enter"], Group.View, "Full screen", Repeat.Ignore),
        new(Command.ToggleTiming, ["F12"], Group.View, "Timing overlay", Repeat.Ignore),
        new(Command.Escape, ["Escape"], Group.Other, "Close overlay, leave full screen, else quit (after pending writes)", Repeat.Ignore),
    ];
}
