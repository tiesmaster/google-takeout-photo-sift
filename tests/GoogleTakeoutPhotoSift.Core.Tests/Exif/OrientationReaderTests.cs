using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Exif;

public class OrientationReaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Theory]
    [InlineData("orientation-1.jpg", 1)]
    [InlineData("orientation-3.jpg", 3)]
    [InlineData("orientation-5.jpg", 5)]
    [InlineData("orientation-6.jpg", 6)]
    [InlineData("orientation-8.jpg", 8)]
    [InlineData("orientation-6.heic", 6)]
    [InlineData("no-exif.jpg", 1)]
    [InlineData("plain.png", 1)]
    public void Reads_the_exif_orientation(string fixture, int expected)
    {
        Assert.Equal(expected, OrientationReader.Read(Fixture(fixture)));
    }

    [Fact]
    public void Reads_from_bytes()
    {
        Assert.Equal(6, OrientationReader.Read(File.ReadAllBytes(Fixture("orientation-6.jpg"))));
    }

    [Fact]
    public void PhotoInfo_reads_camera_and_size()
    {
        var info = PhotoInfo.Read(Fixture("orientation-6.jpg"));
        Assert.Equal("Fixture Synthetic", info.Camera);
        Assert.Equal((32, 16), (info.Width, info.Height));
        Assert.Equal(PhotoInfo.Empty, PhotoInfo.Read(Fixture("missing.jpg")));
    }

    [Theory]
    [InlineData("HTC", "HTC Desire S", "HTC Desire S")]
    [InlineData("Canon", "EOS 600D", "Canon EOS 600D")]
    [InlineData(null, "iPhone 15", "iPhone 15")]
    [InlineData("Apple", null, "Apple")]
    [InlineData(null, null, null)]
    public void Camera_name(string? make, string? model, string? expected)
    {
        Assert.Equal(expected, new PhotoInfo(make, model, null, null, null).Camera);
    }

    [Fact]
    public void Garbage_counts_as_upright()
    {
        Assert.Equal(1, OrientationReader.Read("not an image"u8.ToArray()));
    }
}
