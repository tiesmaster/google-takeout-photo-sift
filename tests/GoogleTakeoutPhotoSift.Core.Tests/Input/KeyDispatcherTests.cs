using Tiesmaster.GoogleTakeoutPhotoSift.Core.Input;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Input;

public class KeyDispatcherTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly ManualTime _time = new();
    private readonly KeyDispatcher _dispatcher;

    public KeyDispatcherTests() => _dispatcher = new KeyDispatcher(time: _time);

    private Command? Press(string key, bool repeat = false) => _dispatcher.Dispatch(KeyChord.Parse(key), repeat).Command;

    [Theory]
    [InlineData("Right", Command.Next)]
    [InlineData("j", Command.Next)]
    [InlineData("Space", Command.Next)]
    [InlineData("Left", Command.Previous)]
    [InlineData("k", Command.Previous)]
    [InlineData("Home", Command.First)]
    [InlineData("G", Command.Last)]
    [InlineData("J", Command.NextAlbum)]
    [InlineData("]", Command.NextAlbum)]
    [InlineData("Shift+Right", Command.NextAlbum)]
    [InlineData("K", Command.PreviousAlbum)]
    [InlineData("[", Command.PreviousAlbum)]
    [InlineData("r", Command.RotateRight)]
    [InlineData("l", Command.RotateLeft)]
    [InlineData("R", Command.Rotate180)]
    [InlineData("d", Command.Delete)]
    [InlineData("Delete", Command.DeleteKeyHint)]
    [InlineData("z", Command.Undo)]
    [InlineData("Ctrl+Z", Command.Undo)]
    [InlineData("F11", Command.ToggleFullScreen)]
    [InlineData("Escape", Command.Escape)]
    public void Keys_map_to_commands(string key, Command expected)
    {
        Assert.Equal(expected, Press(key));
    }

    [Theory]
    [InlineData("g", "g", Command.First)]
    [InlineData("g", "e", Command.AlbumEnd)]
    [InlineData("g", "l", Command.LoosePhotos)]
    public void Sequences(string first, string second, Command expected)
    {
        var start = _dispatcher.Dispatch(KeyChord.Parse(first), false);
        Assert.True(start.Handled);
        Assert.Null(start.Command);
        Assert.StartsWith("g …", start.Hint);
        Assert.Contains("l loose", start.Hint);

        Assert.Equal(expected, Press(second));
        Assert.Null(_dispatcher.Pending);
    }

    [Fact]
    public void An_unknown_second_key_cancels_and_is_not_run_on_its_own()
    {
        Press("g");
        var result = _dispatcher.Dispatch(new KeyChord("r"), false);

        Assert.Null(result.Command);
        Assert.True(result.Handled);
        Assert.Contains("no such shortcut", result.Hint);
        Assert.Equal(Command.RotateRight, Press("r"));
    }

    [Fact]
    public void Escape_cancels_a_sequence()
    {
        Press("g");
        Assert.Null(Press("Escape"));
        Assert.Null(_dispatcher.Pending);
        Assert.Equal(Command.Escape, Press("Escape"));
    }

    [Fact]
    public void A_sequence_lapses_after_the_timeout()
    {
        Press("g");
        _time.Now += TimeSpan.FromSeconds(2);

        Assert.Null(_dispatcher.Pending);
        Assert.Equal(Command.RotateLeft, Press("l"));
    }

    [Fact]
    public void Characters_are_case_sensitive()
    {
        Assert.Equal(Command.Last, Press("G"));
        Assert.Equal(Command.Next, Press("j"));
        Assert.Equal(Command.NextAlbum, Press("J"));
    }

    [Fact]
    public void Navigation_repeats_while_held()
    {
        Assert.Equal(Command.Next, Press("Right", repeat: true));
        Assert.Equal(Command.NextAlbum, Press("J", repeat: true));
    }

    [Fact]
    public void Holding_delete_deletes_one_photo()
    {
        Assert.Equal(Command.Delete, Press("d"));
        Assert.Null(Press("d", repeat: true));
        Assert.Null(Press("z", repeat: true));
        Assert.Null(Press("F11", repeat: true));
    }

    [Fact]
    public void Repeats_never_start_or_complete_a_sequence()
    {
        Assert.Null(Press("g", repeat: true));
        Assert.Null(_dispatcher.Pending);
        Press("g");
        Assert.Null(Press("g", repeat: true));
        Assert.Equal(Command.First, Press("g"));
    }

    [Fact]
    public void Unbound_keys_are_not_handled()
    {
        Assert.False(_dispatcher.Dispatch(new KeyChord("x"), false).Handled);
    }

    [Theory]
    [InlineData("Shift+Right", "Right", false, false, true)]
    [InlineData("Ctrl+Z", "Z", true, false, false)]
    [InlineData("?", "?", false, false, false)]
    [InlineData("+", "+", false, false, false)]
    [InlineData("Ctrl++", "+", true, false, false)]
    public void Parse(string text, string key, bool ctrl, bool alt, bool shift)
    {
        Assert.Equal(new KeyChord(key, ctrl, alt, shift), KeyChord.Parse(text));
    }

    [Fact]
    public void A_key_bound_twice_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new KeyDispatcher([
            new Binding(Command.Next, ["j"], Group.Navigate, "a", Repeat.Allow),
            new Binding(Command.Previous, ["j"], Group.Navigate, "b", Repeat.Allow),
        ]));
    }

    [Fact]
    public void A_sequence_prefix_cannot_also_be_a_key_of_its_own()
    {
        Assert.Throws<ArgumentException>(() => new KeyDispatcher([
            new Binding(Command.First, ["g g"], Group.Navigate, "a", Repeat.Ignore),
            new Binding(Command.Last, ["g"], Group.Navigate, "b", Repeat.Ignore),
        ]));
    }

    [Fact]
    public void The_binding_table_is_valid()
    {
        // Constructing the default dispatcher checks for duplicates and prefix clashes.
        _ = new KeyDispatcher();
        Assert.All(Bindings.All, b =>
        {
            Assert.NotEmpty(b.Keys);
            Assert.False(string.IsNullOrWhiteSpace(b.Description));
        });
        Assert.Equal(Enum.GetValues<Command>().Order(), Bindings.All.Select(b => b.Command).Order());
        // Dead keys under US-International swallow the next key press.
        string[] deadKeys = ["'", "\"", "`", "~", "^"];
        Assert.DoesNotContain(Bindings.All.SelectMany(b => b.Keys).SelectMany(k => k.Split(' ')), deadKeys.Contains);
    }
}
