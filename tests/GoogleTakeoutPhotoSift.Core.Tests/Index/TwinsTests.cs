using Tiesmaster.GoogleTakeoutPhotoSift.Core.Files;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Support;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Index;

public class TwinsTests
{
    private const string Malta = "Vakantie Malta [2013]";

    // Stand-ins for reading the files: headers and fingerprints by file name.
    private readonly Dictionary<string, ImageHeader> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ulong> _prints = new(StringComparer.OrdinalIgnoreCase);

    private TwinReport Find(FakeBatch batch) =>
        Twins.Find(BatchIndex.Load(batch.BatchPath),
            path => _headers.TryGetValue(Path.GetFileName(path), out var h) ? h : null,
            (path, _) => _prints.TryGetValue(Path.GetFileName(path), out var p) ? p : null);

    private void Original(string name, ulong print = 0xF0F0) =>
        (_headers[name], _prints[name]) = (new ImageHeader(3264, 2448, 6), print);

    private void Upright(string name, ulong print = 0xF0F0) =>
        (_headers[name], _prints[name]) = (new ImageHeader(2448, 3264, 1), print);

    [Fact]
    public void The_upright_copy_is_removed_and_the_original_kept()
    {
        using var batch = new FakeBatch()
            .Year("2013-09-13 21.05.57.jpg", taken: 500).Year("IMG_0576.JPG", taken: 500).Year("other.jpg", taken: 600)
            .Album(Malta).File(Malta, "2013-09-13 21.05.57.jpg", taken: 500, content: "pixels of 2013-09-13 21.05.57.jpg")
            .File(Malta, "IMG_0576.JPG", taken: 500, content: "pixels of IMG_0576.JPG");
        Upright("2013-09-13 21.05.57.jpg", 0xF0F0);
        Original("IMG_0576.JPG", 0xF0F1);

        var report = Find(batch);

        var pair = Assert.Single(report.Pairs);
        Assert.Equal("IMG_0576.JPG", pair.Keep.Primary.Name);
        Assert.Equal("2013-09-13 21.05.57.jpg", pair.Remove.Primary.Name);
        Assert.Equal(1, pair.Remove.CopyCount);
        Assert.Equal(1, pair.Distance);
        Assert.Empty(report.Skipped);
    }

    [Fact]
    public void A_180_degree_original_keeps_its_size()
    {
        using var batch = new FakeBatch().Year("a.jpg", taken: 5).Year("IMG_1.JPG", taken: 5);
        (_headers["a.jpg"], _prints["a.jpg"]) = (new ImageHeader(3264, 2448, 1), 7);
        (_headers["IMG_1.JPG"], _prints["IMG_1.JPG"]) = (new ImageHeader(3264, 2448, 3), 7);

        Assert.Equal("IMG_1.JPG", Assert.Single(Find(batch).Pairs).Keep.Primary.Name);
    }

    [Theory]
    [InlineData("orientations 1 and 1")]
    [InlineData("sizes")]
    [InlineData("different pictures")]
    [InlineData("can't read")]
    public void Unclear_cases_are_left_alone(string reason)
    {
        using var batch = new FakeBatch().Year("a.jpg", taken: 5).Year("b.jpg", taken: 5);
        Upright("a.jpg");
        switch (reason)
        {
            case "orientations 1 and 1":
                Upright("b.jpg");
                break;
            case "sizes":
                (_headers["b.jpg"], _prints["b.jpg"]) = (new ImageHeader(4000, 3000, 6), 0xF0F0);
                break;
            case "different pictures":
                Original("b.jpg", 0x0F0F_0000_FFFF);
                break;
            case "can't read":
                break;
        }

        var report = Find(batch);

        Assert.Empty(report.Pairs);
        Assert.Contains(reason, Assert.Single(report.Skipped).Reason);
    }

    [Fact]
    public void Twins_in_different_albums_are_left_alone()
    {
        using var batch = new FakeBatch().Year("a.jpg", taken: 5).Year("IMG_1.JPG", taken: 5)
            .Album(Malta).File(Malta, "IMG_1.JPG", taken: 5);
        Upright("a.jpg");
        Original("IMG_1.JPG");

        var report = Find(batch);

        Assert.Empty(report.Pairs);
        Assert.Contains("different albums", Assert.Single(report.Skipped).Reason);
    }

    [Fact]
    public void Three_photos_at_one_second_are_left_alone()
    {
        using var batch = new FakeBatch().Year("a.jpg", taken: 5).Year("b.jpg", taken: 5).Year("c.jpg", taken: 5);

        Assert.Contains("3 photos", Assert.Single(Find(batch).Skipped).Reason);
    }

    [Fact]
    public void A_dedupe_run_is_undone_as_a_whole_also_after_a_restart()
    {
        using var batch = new FakeBatch().Year("a.jpg", taken: 1).Year("b.jpg", taken: 2).Year("c.jpg", taken: 3);
        var before = batch.Snapshot();
        var index = BatchIndex.Load(batch.BatchPath);
        var deletions = new Deletions(index, batch.BatchesRoot);
        Assert.Null(deletions.Delete(index.Photos[0], "dedupe-1").Work());
        Assert.Null(deletions.Delete(index.Photos[1]).Work());
        Assert.Null(deletions.Delete(index.Photos[2], "dedupe-1").Work());

        // A new session finds the run in the journal and undoes only its deletes.
        var again = new Deletions(BatchIndex.Load(batch.BatchPath), batch.BatchesRoot);
        Assert.Equal("dedupe-1", again.LastRun("dedupe-"));
        var undo = again.UndoRun("dedupe-1");
        Assert.Equal(["Photos from 2016/c.jpg", "Photos from 2016/a.jpg"], undo.Select(u => u.Entry.PhotoRelative));
        Assert.All(undo, u => Assert.Null(u.Work()));

        var after = BatchIndex.Load(batch.BatchPath);
        Assert.Equal(["a.jpg", "c.jpg"], after.Photos.Select(p => p.Primary.Name));
        Assert.Null(new Deletions(after, batch.BatchesRoot).LastRun("dedupe-"));
        Assert.Single(new Deletions(after, batch.BatchesRoot).UndoStack);
        Assert.NotEqual(before, batch.Snapshot());
    }
}
