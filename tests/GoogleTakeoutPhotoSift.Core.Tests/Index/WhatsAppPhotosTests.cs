using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Support;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Index;

public class WhatsAppPhotosTests
{
    [Theory]
    [InlineData(null, false, 1280, 960, true)]
    [InlineData("", false, 1200, 1600, true)]
    [InlineData(null, false, 800, 600, true)]
    [InlineData("Apple", false, 1280, 960, false)]
    [InlineData(null, true, 1280, 960, false)]
    [InlineData(null, false, 3264, 2448, false)]
    [InlineData(null, false, 0, 0, false)]
    public void No_camera_no_date_and_at_most_1600_px(string? make, bool exifDate, int width, int height, bool expected) =>
        Assert.Equal(expected, WhatsAppPhotos.Matches(new ImageTraits(make, exifDate, width, height)));

    [Theory]
    [InlineData(640, 360, true)]
    [InlineData(320, 428, true)]
    [InlineData(1280, 720, false)]
    [InlineData(480, 800, false)]
    [InlineData(null, null, false)]
    public void Videos_at_most_640_px(int? width, int? height, bool expected) =>
        Assert.Equal(expected, WhatsAppPhotos.Matches(new VideoInfo(TimeSpan.FromSeconds(10), width, height)));

    [Fact]
    public void Partner_camera_by_model()
    {
        using var batch = new FakeBatch().Year("a.jpg").Year("b.jpg").Year("c.jpg");
        var index = BatchIndex.Load(batch.BatchPath);
        var models = new Dictionary<string, string?> { ["a.jpg"] = "GT-I9195", ["b.jpg"] = "iPhone 5", ["c.jpg"] = null };

        WhatsAppPhotos.Mark(index, ["gt-i9195 "], path => new ImageTraits("x", true, 4000, 3000, models[Path.GetFileName(path)]));

        Assert.Equal([true, false, false], index.Photos.Select(p => p.IsFromPartner));
    }

    [Theory]
    [InlineData("orientation-6.jpg")]
    [InlineData("no-exif.jpg")]
    public void Traits_come_from_the_headers(string fixture)
    {
        var traits = WhatsAppPhotos.ReadTraits(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture));

        Assert.NotNull(traits);
        Assert.True(traits.Value.Width > 0 && traits.Value.Height > 0);
    }
}
