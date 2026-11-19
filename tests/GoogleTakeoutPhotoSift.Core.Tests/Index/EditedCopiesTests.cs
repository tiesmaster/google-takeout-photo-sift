using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Support;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Index;

public class EditedCopiesTests
{
    private const string Trip = "Vakantie IJsland [2014]";

    // Stand-ins for reading the files: headers and samples by file name. A sample is 4 bytes of one level.
    private readonly Dictionary<string, ImageHeader> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte> _levels = new(StringComparer.OrdinalIgnoreCase);

    private EditedReport Find(FakeBatch batch) =>
        EditedCopies.Find(BatchIndex.Load(batch.BatchPath),
            path => _headers.TryGetValue(Path.GetFileName(path), out var h) ? h : null,
            (path, _) => _levels.TryGetValue(Path.GetFileName(path), out var level) ? [level, level, level, level] : null);

    private void Picture(string name, byte level, int width = 3264, int height = 2448, int orientation = 1) =>
        (_headers[name], _levels[name]) = (new ImageHeader(width, height, orientation), level);

    [Theory]
    [InlineData("IMG_1372-edited.JPG", "IMG_1372")]
    [InlineData("IMG_1391-EFFECTS-edited.jpg", "IMG_1391-EFFECTS")]
    [InlineData("IMG_1-bewerkt.jpg", "IMG_1")]
    [InlineData("IMG_1.JPG", null)]
    [InlineData("edited.jpg", null)]
    public void Edited_names(string name, string? stem) => Assert.Equal(stem, EditedCopies.OriginalStem(name));

    [Fact]
    public void A_mild_edit_is_dropped_and_the_original_kept()
    {
        using var batch = new FakeBatch("2014").Year("IMG_1.JPG").Year("IMG_1-edited.JPG").Year("IMG_2.JPG");
        Picture("IMG_1.JPG", 100);
        Picture("IMG_1-edited.JPG", 105);

        var report = Find(batch);

        var pair = Assert.Single(report.Pairs);
        Assert.Equal("IMG_1.JPG", pair.Original.Primary.Name);
        Assert.Equal("IMG_1-edited.JPG", pair.Edited.Primary.Name);
        Assert.Equal(5, pair.Difference);
        Assert.Empty(report.Skipped);
    }

    [Fact]
    public void A_turned_original_compares_by_its_shown_shape()
    {
        using var batch = new FakeBatch("2014").Year("IMG_1.JPG").Year("IMG_1-edited.JPG");
        Picture("IMG_1.JPG", 100, orientation: 6);
        Picture("IMG_1-edited.JPG", 100, width: 2448, height: 3264);

        Assert.Single(Find(batch).Pairs);
    }

    [Theory]
    [InlineData(130, 3264, 2448, "strongly changed")]
    [InlineData(100, 2448, 2448, "shape changed")]
    [InlineData(100, 2448, 3264, "shape changed")]
    public void A_deliberate_edit_is_left_alone(byte level, int width, int height, string reason)
    {
        using var batch = new FakeBatch("2014").Year("IMG_1.JPG").Year("IMG_1-edited.JPG");
        Picture("IMG_1.JPG", 100);
        Picture("IMG_1-edited.JPG", level, width, height);

        var report = Find(batch);

        Assert.Empty(report.Pairs);
        Assert.Contains(reason, Assert.Single(report.Skipped).Reason);
    }

    [Fact]
    public void An_edit_whose_original_is_missing_or_unreadable_is_left_alone()
    {
        using var batch = new FakeBatch("2014").Year("IMG_1-edited.JPG").Year("IMG_2.JPG").Year("IMG_2-edited.JPG");
        Picture("IMG_1-edited.JPG", 100);
        Picture("IMG_2.JPG", 100);

        var reasons = Find(batch).Skipped.Select(s => s.Reason).ToList();

        Assert.Equal(["no original next to it", "can't read one of the files"], reasons);
    }

    [Fact]
    public void Album_copies_go_along_only_when_the_original_is_in_those_albums_too()
    {
        using var batch = new FakeBatch("2014").Year("IMG_1.JPG", taken: 1).Year("IMG_1-edited.JPG", taken: 1)
            .Year("IMG_2.JPG", taken: 2).Year("IMG_2-edited.JPG", taken: 2)
            .Album(Trip)
            .File(Trip, "IMG_1.JPG", taken: 1).File(Trip, "IMG_1-edited.JPG", taken: 1)
            .File(Trip, "IMG_2-edited.JPG", taken: 2);
        foreach (var name in new[] { "IMG_1.JPG", "IMG_1-edited.JPG", "IMG_2.JPG", "IMG_2-edited.JPG" })
            Picture(name, 100);

        var report = Find(batch);

        var pair = Assert.Single(report.Pairs);
        Assert.Equal("IMG_1-edited.JPG", pair.Edited.Primary.Name);
        Assert.Equal(1, pair.Edited.CopyCount);
        Assert.Equal($"only the edited one is in {Trip}", Assert.Single(report.Skipped).Reason);
    }

    [Fact]
    public void Mean_difference_is_per_byte() =>
        Assert.Equal(2.5, EditedCopies.MeanDifference([0, 10, 255, 255], [0, 0, 255, 255]));
}
