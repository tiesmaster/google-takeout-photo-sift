using Tiesmaster.GoogleTakeoutPhotoSift.Core.Input;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Input;

public class KeyDispatcherTests
{
    private readonly KeyDispatcher _dispatcher = new();

    [Theory]
    [InlineData("Right", Command.Next)]
    [InlineData("j", Command.Next)]
    [InlineData("Space", Command.Next)]
    [InlineData("Left", Command.Previous)]
    [InlineData("k", Command.Previous)]
    [InlineData("Home", Command.First)]
    [InlineData("G", Command.Last)]
    [InlineData("F11", Command.ToggleFullScreen)]
    [InlineData("Escape", Command.Escape)]
    public void Keys_map_to_commands(string key, Command expected)
    {
        Assert.Equal(expected, _dispatcher.Dispatch(KeyChord.Parse(key), isRepeat: false));
    }

    [Fact]
    public void Characters_are_case_sensitive()
    {
        Assert.Equal(Command.Last, _dispatcher.Dispatch(new KeyChord("G"), false));
        Assert.Null(_dispatcher.Dispatch(new KeyChord("g"), false));
    }

    [Fact]
    public void Navigation_repeats_while_held()
    {
        Assert.Equal(Command.Next, _dispatcher.Dispatch(new KeyChord("Right"), isRepeat: true));
    }

    [Fact]
    public void Other_commands_ignore_key_repeat()
    {
        Assert.Null(_dispatcher.Dispatch(new KeyChord("F11"), isRepeat: true));
        Assert.Null(_dispatcher.Dispatch(new KeyChord("Home"), isRepeat: true));
    }

    [Fact]
    public void Unbound_keys_give_nothing()
    {
        Assert.Null(_dispatcher.Dispatch(new KeyChord("x"), false));
        Assert.False(_dispatcher.IsBound(new KeyChord("x")));
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
    public void The_binding_table_is_valid()
    {
        // Constructing the default dispatcher checks for duplicates.
        _ = new KeyDispatcher();
        Assert.All(Bindings.All, b =>
        {
            Assert.NotEmpty(b.Keys);
            Assert.False(string.IsNullOrWhiteSpace(b.Description));
        });
        Assert.Equal(Enum.GetValues<Command>().Order(), Bindings.All.Select(b => b.Command).Order());
        // Dead keys under US-International swallow the next key press.
        string[] deadKeys = ["'", "\"", "`", "~", "^"];
        Assert.DoesNotContain(Bindings.All.SelectMany(b => b.Keys), deadKeys.Contains);
    }
}
