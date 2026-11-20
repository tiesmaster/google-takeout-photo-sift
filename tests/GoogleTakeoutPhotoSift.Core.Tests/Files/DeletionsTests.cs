using Tiesmaster.GoogleTakeoutPhotoSift.Core.Files;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Support;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Files;

public class DeletionsTests
{
    private const string Trip = "Vakantie IJsland [2016]";

    private static (BatchIndex Index, Deletions Deletions) Load(FakeBatch batch)
    {
        var index = BatchIndex.Load(batch.BatchPath);
        return (index, new Deletions(index, batch.BatchesRoot));
    }

    private static Photo PhotoNamed(BatchIndex index, string name) => index.Photos.Single(p => p.Primary.Name == name);

    private static void Run((DeleteEntry Entry, Func<string?> Work)? op) => Assert.Null(op!.Value.Work());

    [Fact]
    public void Delete_moves_the_photo_its_copies_and_sidecars_to_the_deleted_tree()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100).Year("IMG_2.jpg", taken: 200)
            .Album(Trip).File(Trip, "IMG_1.jpg", taken: 100);
        var before = batch.Snapshot();
        var (index, deletions) = Load(batch);

        Run(deletions.Delete(PhotoNamed(index, "IMG_1.jpg")));

        var after = batch.Snapshot();
        string[] moved =
        [
            "Photos from 2016/IMG_1.jpg", "Photos from 2016/IMG_1.jpg.supplemental-metadata.json",
            $"{Trip}/IMG_1.jpg", $"{Trip}/IMG_1.jpg.supplemental-metadata.json",
        ];
        foreach (var file in moved)
        {
            Assert.DoesNotContain($"2016/Takeout/Google Photos/{file}", after.Keys);
            // Same content and modified time in the deleted tree.
            Assert.Equal(before[$"2016/Takeout/Google Photos/{file}"], after[$"deleted/2016/Takeout/Google Photos/{file}"]);
        }
        Assert.Contains("2016/Takeout/Google Photos/Photos from 2016/IMG_2.jpg", after.Keys);
        Assert.Contains($"2016/Takeout/Google Photos/{Trip}/metadata.json", after.Keys);
    }

    [Fact]
    public void Undo_restores_everything()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100).Album(Trip).File(Trip, "IMG_1.jpg", taken: 100);
        var before = batch.Snapshot();
        var (index, deletions) = Load(batch);

        Run(deletions.Delete(PhotoNamed(index, "IMG_1.jpg")));
        Run(deletions.Undo());

        Assert.Equal(before, WithoutJournal(batch.Snapshot()));
        Assert.Empty(deletions.UndoStack);
    }

    [Theory]
    [InlineData("IMG_1.jpg", "IMG_1-edited.jpg")]
    [InlineData("IMG_1-edited.jpg", "IMG_1.jpg")]
    public void A_sidecar_shared_with_an_edited_copy_is_copied_then_deduplicated(string first, string second)
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100).Year("IMG_1-edited.jpg", noJson: true);
        var before = batch.Snapshot();
        var (index, deletions) = Load(batch);
        const string json = "Photos from 2016/IMG_1.jpg.supplemental-metadata.json";

        var (entry1, work1) = deletions.Delete(PhotoNamed(index, first));
        Assert.Null(work1());
        Assert.Equal(MoveKind.Copy, entry1.Records!.Single(r => r.Source.EndsWith(".json")).Kind);
        Assert.Contains($"2016/Takeout/Google Photos/{json}", batch.Snapshot().Keys);

        var (entry2, work2) = deletions.Delete(PhotoNamed(index, second));
        Assert.Null(work2());
        Assert.Equal(MoveKind.Dedup, entry2.Records!.Single(r => r.Source.EndsWith(".json")).Kind);
        var afterBoth = batch.Snapshot();
        Assert.DoesNotContain($"2016/Takeout/Google Photos/{json}", afterBoth.Keys);
        Assert.Contains($"deleted/2016/Takeout/Google Photos/{json}", afterBoth.Keys);

        Run(deletions.Undo());
        Run(deletions.Undo());
        Assert.Equal(before, WithoutJournal(batch.Snapshot()));
    }

    [Fact]
    public void Delete_fails_and_changes_nothing_when_the_destination_differs()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100).Album(Trip).File(Trip, "IMG_1.jpg", taken: 100);
        // Something else already sits where the album copy would go.
        var clash = Path.Combine(batch.DeletedRoot, Trip, "IMG_1.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(clash)!);
        File.WriteAllText(clash, "another photo");
        var before = batch.Snapshot();
        var (index, deletions) = Load(batch);

        var (_, work) = deletions.Delete(PhotoNamed(index, "IMG_1.jpg"));
        var error = work();

        Assert.NotNull(error);
        Assert.Contains("already exists", error);
        Assert.Equal(before, batch.Snapshot());
        Assert.All(index.SidecarUsers.Values, users => Assert.Single(users));
    }

    [Fact]
    public void Several_deletes_undone_in_reverse_leave_the_tree_as_it_was()
    {
        using var batch = new FakeBatch().Year("A.jpg", taken: 1).Year("B.jpg", taken: 2).Year("C.jpg", taken: 3)
            .Album(Trip).File(Trip, "B.jpg", taken: 2);
        var before = batch.Snapshot();
        var (index, deletions) = Load(batch);

        foreach (var name in new[] { "B.jpg", "A.jpg", "C.jpg" })
            Run(deletions.Delete(PhotoNamed(index, name)));
        Assert.Single(index.Photos.Select(p => p.Primary.Path).Where(File.Exists).DefaultIfEmpty("none"));
        for (var i = 0; i < 3; i++)
            Run(deletions.Undo());

        Assert.Equal(before, WithoutJournal(batch.Snapshot()));
        Assert.Null(deletions.Undo());
    }

    [Fact]
    public void A_live_photo_video_moves_along_and_comes_back()
    {
        using var batch = new FakeBatch().Year("IMG_9.HEIC", taken: 5).Year("IMG_9.MOV", taken: 5).Year("IMG_10.jpg", taken: 6);
        var before = batch.Snapshot();
        var (index, deletions) = Load(batch);

        Run(deletions.Delete(PhotoNamed(index, "IMG_9.HEIC")));
        var after = batch.Snapshot();
        Assert.Contains("deleted/2016/Takeout/Google Photos/Photos from 2016/IMG_9.MOV", after.Keys);
        Assert.Contains("deleted/2016/Takeout/Google Photos/Photos from 2016/IMG_9.MOV.supplemental-metadata.json", after.Keys);
        Assert.DoesNotContain("2016/Takeout/Google Photos/Photos from 2016/IMG_9.MOV", after.Keys);

        Run(deletions.Undo());
        Assert.Equal(before, WithoutJournal(batch.Snapshot()));
    }

    [Fact]
    public void A_video_on_its_own_is_deleted_with_its_album_copy_and_comes_back()
    {
        using var batch = new FakeBatch().Year("talk.mp4", taken: 5).Year("IMG_10.jpg", taken: 6)
            .Album("Conferentie").File("Conferentie", "talk.mp4", taken: 5);
        var before = batch.Snapshot();
        var (index, deletions) = Load(batch);

        Run(deletions.Delete(PhotoNamed(index, "talk.mp4")));
        var after = batch.Snapshot();
        Assert.Contains("deleted/2016/Takeout/Google Photos/Photos from 2016/talk.mp4", after.Keys);
        Assert.Contains("deleted/2016/Takeout/Google Photos/Photos from 2016/talk.mp4.supplemental-metadata.json", after.Keys);
        Assert.Contains("deleted/2016/Takeout/Google Photos/Conferentie/talk.mp4", after.Keys);

        Run(deletions.Undo());
        Assert.Equal(before, WithoutJournal(batch.Snapshot()));
    }

    [Fact]
    public void The_journal_brings_undo_back_after_a_restart()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100).Year("IMG_2.jpg", taken: 200);
        var before = batch.Snapshot();
        var (index, deletions) = Load(batch);
        Run(deletions.Delete(PhotoNamed(index, "IMG_1.jpg")));
        Run(deletions.Delete(PhotoNamed(index, "IMG_2.jpg")));
        Run(deletions.Undo());

        // A new session: only the delete that wasn't undone can be undone.
        var (index2, deletions2) = Load(batch);
        var entry = Assert.Single(deletions2.UndoStack);
        Assert.Equal("Photos from 2016/IMG_1.jpg", entry.PhotoRelative);
        Assert.Null(entry.Photo);
        Assert.DoesNotContain(index2.Photos, p => p.Primary.Name == "IMG_1.jpg");

        Run(deletions2.Undo());
        Assert.Equal(before, WithoutJournal(batch.Snapshot()));
        Assert.Empty(new Deletions(BatchIndex.Load(batch.BatchPath), batch.BatchesRoot).UndoStack);
    }

    [Fact]
    public void Use_finds_photos_again_in_a_reloaded_index()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100).Year("IMG_2.jpg", taken: 200);
        var (index, deletions) = Load(batch);
        Run(deletions.Delete(PhotoNamed(index, "IMG_1.jpg")));
        Run(deletions.Delete(PhotoNamed(index, "IMG_2.jpg")));
        Run(deletions.Undo());

        var reloaded = BatchIndex.Load(batch.BatchPath);
        deletions.Use(reloaded);

        Assert.Null(Assert.Single(deletions.UndoStack).Photo);
        Run(deletions.Undo());
        Assert.Equal(2, BatchIndex.Load(batch.BatchPath).Photos.Count);
    }

    private static SortedDictionary<string, string> WithoutJournal(SortedDictionary<string, string> snapshot)
    {
        snapshot.Remove("deleted/2016/sift-journal.jsonl");
        return snapshot;
    }
}
