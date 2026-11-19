using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Index;

public class ByteSizeTests
{
    [Theory]
    [InlineData(0, "0 MB")]
    [InlineData(812L << 20, "812 MB")]
    [InlineData((1L << 30) - 1, "1024 MB")]
    [InlineData(1L << 30, "1.00 GB")]
    [InlineData(43L << 28, "10.75 GB")]
    public void Megabytes_below_a_gigabyte_then_gigabytes(long bytes, string expected) =>
        Assert.Equal(expected, ByteSize.Format(bytes));
}
