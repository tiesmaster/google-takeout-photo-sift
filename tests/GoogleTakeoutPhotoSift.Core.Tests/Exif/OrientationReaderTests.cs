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
    public void Garbage_counts_as_upright()
    {
        Assert.Equal(1, OrientationReader.Read("not an image"u8.ToArray()));
    }
}
