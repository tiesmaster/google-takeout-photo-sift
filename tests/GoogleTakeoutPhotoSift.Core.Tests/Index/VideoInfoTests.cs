using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Index;

public class VideoInfoTests
{
    [Theory]
    [InlineData(36, "0:36")]
    [InlineData(1336, "22:16")]
    [InlineData(3900, "1:05:00")]
    public void Length_in_minutes_and_seconds(int seconds, string expected) =>
        Assert.Equal(expected, VideoInfo.FormatDuration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void An_unreadable_file_gives_nothing()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a video");
            Assert.Equal(new VideoInfo(null, null, null), VideoInfo.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
