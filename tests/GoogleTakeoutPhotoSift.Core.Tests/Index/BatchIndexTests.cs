using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Support;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Index;

public class BatchIndexTests
{
    private const string Trip = "Vakantie IJsland [2016]";
    private const string Party = "Feestje [2016]";

    private static List<List<string>> Groups(FakeBatch batch, IndexOptions? options = null) =>
        BatchIndex.Load(batch.BatchPath, options).Photos
            .Select(p => p.Files.Select(f => batch.Relative(f.Path)).ToList())
            .ToList();

    [Fact]
    public void Same_name_and_same_date_is_a_copy()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100).Album(Trip).File(Trip, "IMG_1.jpg", taken: 100, content: "other bytes");

        Assert.Equal([["Photos from 2016/IMG_1.jpg", $"{Trip}/IMG_1.jpg"]], Groups(batch));
    }

    [Fact]
    public void Same_name_without_json_but_identical_bytes_is_a_copy()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", noJson: true, content: "same")
            .Album(Trip).File(Trip, "IMG_1.jpg", noJson: true, content: "same");

        Assert.Equal([["Photos from 2016/IMG_1.jpg", $"{Trip}/IMG_1.jpg"]], Groups(batch));
    }

    [Fact]
    public void Same_name_with_different_date_and_bytes_is_another_photo()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100, content: "a")
            .Album(Trip).File(Trip, "IMG_1.jpg", taken: 200, content: "b");

        Assert.Equal([["Photos from 2016/IMG_1.jpg"], [$"{Trip}/IMG_1.jpg"]], Groups(batch));
    }

    [Fact]
    public void Same_name_with_different_date_but_identical_bytes_is_a_copy()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100, content: "same")
            .Album(Trip).File(Trip, "IMG_1.jpg", taken: 200, content: "same");

        Assert.Single(Groups(batch));
    }

    [Fact]
    public void Name_match_ignores_case()
    {
        using var batch = new FakeBatch().Year("IMG_1.JPG", taken: 100).Album(Trip).File(Trip, "img_1.jpg", taken: 100);

        Assert.Equal([["Photos from 2016/IMG_1.JPG", $"{Trip}/img_1.jpg"]], Groups(batch));
    }

    [Fact]
    public void Photo_in_two_albums_has_both_copies()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100)
            .Album(Trip).File(Trip, "IMG_1.jpg", taken: 100)
            .Album(Party).File(Party, "IMG_1.jpg", taken: 100);

        Assert.Equal([["Photos from 2016/IMG_1.jpg", $"{Party}/IMG_1.jpg", $"{Trip}/IMG_1.jpg"]], Groups(batch));
    }

    [Fact]
    public void Album_only_photo_in_two_albums_is_one_photo()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100)
            .Album(Trip).File(Trip, "SHARED.jpg", taken: 500)
            .Album(Party).File(Party, "SHARED.jpg", taken: 500);

        Assert.Equal([["Photos from 2016/IMG_1.jpg"], [$"{Party}/SHARED.jpg", $"{Trip}/SHARED.jpg"]], Groups(batch));
    }

    [Fact]
    public void Python_mode_keeps_album_only_copies_separate()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100)
            .Album(Trip).File(Trip, "SHARED.jpg", taken: 500)
            .Album(Party).File(Party, "SHARED.jpg", taken: 500);

        Assert.Equal([["Photos from 2016/IMG_1.jpg"], [$"{Party}/SHARED.jpg"], [$"{Trip}/SHARED.jpg"]],
            Groups(batch, IndexOptions.Python));
    }

    [Fact]
    public void Order_is_year_folder_by_name_then_album_only_photos()
    {
        using var batch = new FakeBatch()
            .Year("b.jpg", taken: 1).Year("A.jpg", taken: 2).Year("c.jpg", taken: 3)
            .Album(Trip).File(Trip, "z.jpg", taken: 9).File(Trip, "b.jpg", taken: 1);

        Assert.Equal([["Photos from 2016/A.jpg"], ["Photos from 2016/b.jpg", $"{Trip}/b.jpg"], ["Photos from 2016/c.jpg"], [$"{Trip}/z.jpg"]],
            Groups(batch));
    }

    [Fact]
    public void Videos_are_skipped_but_their_sidecars_count_as_used()
    {
        using var batch = new FakeBatch()
            .Year("IMG_1.jpg", taken: 1, json: "IMG_1.json")
            .Year("IMG_1.mp4", noJson: true);

        var index = BatchIndex.Load(batch.BatchPath);

        var photo = Assert.Single(index.Photos);
        Assert.Equal("IMG_1.jpg", photo.Primary.Name);
        var users = index.SidecarUsers[batch.PathOf(batch.YearFolder, "IMG_1.json")];
        Assert.Equal(2, users.Count);
    }

    [Fact]
    public void Edited_copy_shares_the_sidecar_and_is_its_own_photo()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 7).Year("IMG_1-edited.jpg", noJson: true);

        var index = BatchIndex.Load(batch.BatchPath);

        Assert.Equal(2, index.Photos.Count);
        var json = batch.PathOf(batch.YearFolder, "IMG_1.jpg.supplemental-metadata.json");
        Assert.All(index.Photos, p => Assert.Equal(json, p.Primary.SidecarPath));
        Assert.All(index.Photos, p => Assert.Equal(7, p.Primary.TakenUnix));
        Assert.Equal(2, index.SidecarUsers[json].Count);
    }

    [Fact]
    public void Album_title_comes_from_metadata_json()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg")
            .Album("DVR_ Drakenboot [2013]", title: "DVR: Drakenboot [2013]").File("DVR_ Drakenboot [2013]", "x.jpg");

        var album = Assert.Single(BatchIndex.Load(batch.BatchPath).Albums);
        Assert.Equal("DVR: Drakenboot [2013]", album.Title);
    }

    [Fact]
    public void Album_without_metadata_uses_the_folder_name()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg").File("Losse map", "x.jpg");

        Assert.Equal("Losse map", Assert.Single(BatchIndex.Load(batch.BatchPath).Albums).Title);
    }

    [Fact]
    public void Total_size_counts_each_photo_once_and_skips_deleted_ones()
    {
        using var batch = new FakeBatch().Year("IMG_1.jpg", taken: 100, content: "1234")
            .Year("IMG_2.jpg", content: "123456").Album(Trip).File(Trip, "IMG_1.jpg", taken: 100, content: "1234");
        var photos = BatchIndex.Load(batch.BatchPath).Photos;

        Assert.Equal(10, ByteSize.Total(photos));
        photos[1].Deleted = true;
        Assert.Equal(4, ByteSize.Total(photos));
    }
}
