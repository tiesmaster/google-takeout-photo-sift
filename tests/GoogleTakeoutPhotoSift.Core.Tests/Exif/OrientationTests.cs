using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Exif;

public class OrientationTests
{
    [Theory]
    [InlineData(1, 90, 6)]
    [InlineData(6, 90, 3)]
    [InlineData(3, 90, 8)]
    [InlineData(8, 90, 1)]
    [InlineData(1, -90, 8)]
    [InlineData(6, -90, 1)]
    [InlineData(1, 180, 3)]
    [InlineData(6, 180, 8)]
    [InlineData(3, 360, 3)]
    public void Rotate_turns_non_mirrored_orientations(int orientation, int degrees, int expected)
    {
        Assert.Equal(expected, Orientation.Rotate(orientation, degrees));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(0)]
    [InlineData(9)]
    public void Rotate_refuses_mirrored_and_unknown_orientations(int orientation)
    {
        Assert.Null(Orientation.Rotate(orientation, 90));
    }

    [Fact]
    public void Rotate_only_accepts_quarter_turns()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Orientation.Rotate(1, 45));
    }
}
