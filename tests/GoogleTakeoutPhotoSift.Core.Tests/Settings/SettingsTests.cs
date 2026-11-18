using Tiesmaster.GoogleTakeoutPhotoSift.Core.Settings;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Settings;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("photo-sift-settings").FullName;

    [Fact]
    public void CommandLine_with_year_only()
    {
        Assert.Equal(new CommandLine("2016", null, ReviewOrderMode.Python, false, null), CommandLine.Parse(["2016"]));
    }

    [Fact]
    public void CommandLine_with_all_options()
    {
        var parsed = CommandLine.Parse([@"C:\b\2012", "--start", "120", "--order", "python", "--timing", "--batches-root", @"D:\x", "--bench"]);
        Assert.Equal(new CommandLine(@"C:\b\2012", 120, ReviewOrderMode.Python, true, @"D:\x", Bench: true), parsed);
    }

    [Theory]
    [InlineData]
    [InlineData("--start", "5")]
    [InlineData("2016", "--start")]
    [InlineData("2016", "--start", "0")]
    [InlineData("2016", "--order", "name")]
    [InlineData("2016", "--nope")]
    [InlineData("2016", "2017")]
    public void CommandLine_errors(params string[] args)
    {
        Assert.Throws<FormatException>(() => CommandLine.Parse(args));
    }

    [Fact]
    public void Settings_default_when_missing_and_read_partial_files()
    {
        Assert.Equal(new SiftSettings(), SiftSettings.Load(Path.Combine(_dir, "missing.json")));

        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            {
              // comments are fine
              "prefetchAhead": 6,
              "BatchesRoot": "D:\\batches",
            }
            """);
        var settings = SiftSettings.Load(path);
        Assert.Equal(6, settings.PrefetchAhead);
        Assert.Equal(@"D:\batches", settings.BatchesRoot);
        Assert.Equal(2, settings.PrefetchBehind);
    }

    [Fact]
    public void Broken_settings_fall_back_to_defaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");
        Assert.Equal(new SiftSettings(), SiftSettings.Load(path));
    }

    [Fact]
    public void ResumeState_round_trips_and_ignores_case_of_batch_paths()
    {
        var path = Path.Combine(_dir, "state.json");
        var state = new ResumeState();
        state.Batches[@"C:\B\2016"] = new ResumeState.Position("Photos from 2016/IMG_1.jpg", 41);
        state.Save(path);

        var loaded = ResumeState.Load(path);
        Assert.Equal(new ResumeState.Position("Photos from 2016/IMG_1.jpg", 41), loaded.Batches[@"c:\b\2016"]);
        Assert.False(File.Exists(path + ".tmp"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
