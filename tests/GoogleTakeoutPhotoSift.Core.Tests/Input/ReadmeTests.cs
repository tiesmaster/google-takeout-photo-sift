using Tiesmaster.GoogleTakeoutPhotoSift.Core.Input;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Input;

/// <summary>
/// The README's shortcut table must match the binding table. Run with UPDATE_README=1 to rewrite it.
/// </summary>
public class ReadmeTests
{
    private const string Start = "<!-- shortcuts -->";
    private const string End = "<!-- /shortcuts -->";

    [Fact]
    public void The_readme_lists_every_shortcut()
    {
        var readme = FindReadme();
        Assert.SkipWhen(readme is null, "README.md not found");
        var text = File.ReadAllText(readme!).ReplaceLineEndings("\n");
        var start = text.IndexOf(Start, StringComparison.Ordinal);
        var end = text.IndexOf(End, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"README.md needs a {Start} … {End} block");

        var expected = $"{Start}\n{Bindings.MarkdownTable()}\n{End}";
        var actual = text[start..(end + End.Length)];
        if (actual != expected && Environment.GetEnvironmentVariable("UPDATE_README") == "1")
        {
            File.WriteAllText(readme!, text[..start] + expected + text[(end + End.Length)..]);
            return;
        }
        Assert.True(actual == expected, $"README.md's shortcut table is out of date; run the tests with UPDATE_README=1, or paste:\n{expected}");
    }

    private static string? FindReadme()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "README.md");
            if (File.Exists(path) && File.Exists(Path.Combine(dir.FullName, "GoogleTakeoutPhotoSift.slnx")))
                return path;
        }
        return null;
    }
}
