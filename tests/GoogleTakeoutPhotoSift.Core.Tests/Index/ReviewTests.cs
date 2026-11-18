using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Support;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Index;

public class ReviewTests
{
    private const string Trip = "Vakantie IJsland [2016]";
    private const string Party = "Feestje [2016]";
    private const string Old = "Vakantie England [2012]";

    private static Review Load(FakeBatch batch, Func<string, long?>? exif = null) =>
        Review.AlbumFirst(BatchIndex.Load(batch.BatchPath), exif ?? (_ => null));

    private static List<string> Names(Review review) => review.Photos.Select(p => $"{p.Primary.Folder}/{p.Primary.Name}").ToList();

    [Fact]
    public void Albums_come_first_ordered_by_their_first_photo_then_the_loose_photos()
    {
        using var batch = new FakeBatch()
            .Year("a.jpg", taken: 10).Year("b.jpg", taken: 20).Year("c.jpg", taken: 30).Year("loose.jpg", taken: 5)
            .Album(Party).File(Party, "c.jpg", taken: 30)
            .Album(Trip).File(Trip, "b.jpg", taken: 20).File(Trip, "a.jpg", taken: 10);

        var review = Load(batch);

        Assert.Equal([Trip, Party, "Loose photos"], review.Sections.Select(s => s.Title));
        Assert.Equal(["Photos from 2016/a.jpg", "Photos from 2016/b.jpg", "Photos from 2016/c.jpg", "Photos from 2016/loose.jpg"],
            Names(review));
        Assert.Equal(2, review.AlbumCount);
        Assert.Equal([0, 2, 3], review.Sections.Select(s => s.Start));
    }

    [Fact]
    public void A_photo_in_two_albums_shows_in_the_first_and_lists_the_other()
    {
        using var batch = new FakeBatch()
            .Year("x.jpg", taken: 50).Year("early.jpg", taken: 1)
            .Album(Trip).File(Trip, "x.jpg", taken: 50).File(Trip, "early.jpg", taken: 1)
            .Album(Party).File(Party, "x.jpg", taken: 50);

        var review = Load(batch);

        var x = review.Photos.Single(p => p.Primary.Name == "x.jpg");
        Assert.Equal([Trip, Party], review.AlbumsOf(x).Select(a => a.Title));
        Assert.Equal(Trip, review.Sections[review.SectionIndexOf(x)].Title);
        Assert.Single(review.Photos, p => p.Primary.Name == "x.jpg");
        // Party has no photos of its own: no section, but the picker lists it.
        Assert.DoesNotContain(review.Sections, s => s.Title == Party);
        var party = review.Albums.Single(a => a.Album.Title == Party);
        Assert.Null(party.Section);
        Assert.Equal(Trip, party.ShownIn!.Title);
        Assert.Equal(1, party.MemberCount);
    }

    [Fact]
    public void An_album_from_an_earlier_year_sorts_by_its_photos_in_this_batch()
    {
        using var batch = new FakeBatch()
            .Year("summer.jpg", taken: 900).Year("spring.jpg", taken: 500)
            .Album(Trip).File(Trip, "summer.jpg", taken: 900)
            .Album(Old).File(Old, "spring.jpg", taken: 500);

        Assert.Equal([Old, Trip], Load(batch).Sections.Select(s => s.Title));
    }

    [Fact]
    public void Within_a_section_by_date_then_name_and_undated_photos_last()
    {
        using var batch = new FakeBatch()
            .Year("b.jpg", taken: 7).Year("a.jpg", taken: 7).Year("first.jpg", taken: 1)
            .Year("nodate.jpg", noJson: true).Year("exif.jpg", noJson: true);

        var review = Load(batch, path => path.EndsWith("exif.jpg") ? 3 : null);

        Assert.Equal(["first.jpg", "exif.jpg", "a.jpg", "b.jpg", "nodate.jpg"], review.Photos.Select(p => p.Primary.Name));
    }

    [Fact]
    public void Album_ties_on_date_are_broken_by_title()
    {
        using var batch = new FakeBatch()
            .Year("p.jpg", taken: 5).Year("q.jpg", taken: 5)
            .Album("Zee").File("Zee", "p.jpg", taken: 5)
            .Album("Aap").File("Aap", "q.jpg", taken: 5);

        Assert.Equal(["Aap", "Zee"], Load(batch).Sections.Select(s => s.Title));
    }

    [Fact]
    public void Album_only_photos_are_reviewed_in_their_album()
    {
        using var batch = new FakeBatch()
            .Year("y.jpg", taken: 5)
            .Album(Trip).File(Trip, "shared.jpg", taken: 1)
            .Album(Party).File(Party, "shared.jpg", taken: 1);

        var review = Load(batch);

        var shared = review.Photos.Single(p => p.Primary.Name == "shared.jpg");
        Assert.Equal(2, shared.Files.Count);
        Assert.Equal(2, review.AlbumsOf(shared).Count);
        Assert.Equal(2, review.Photos.Count);
    }

    [Fact]
    public void Python_mode_is_one_section_in_the_index_order()
    {
        using var batch = new FakeBatch().Year("b.jpg", taken: 1).Year("a.jpg", taken: 2).Album(Trip).File(Trip, "a.jpg", taken: 2);

        var index = BatchIndex.Load(batch.BatchPath, IndexOptions.Python);
        var review = Review.Python(index);

        Assert.True(review.IsPython);
        Assert.Equal(index.Photos, review.Photos);
        Assert.Single(review.Sections);
        Assert.Equal([Trip], review.AlbumsOf(review.Photos[0]).Select(a => a.Title));
    }
}
