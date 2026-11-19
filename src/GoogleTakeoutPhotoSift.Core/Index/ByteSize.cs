using System.Globalization;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

public static class ByteSize
{
    /// <summary>
    /// GB from 1 GB up, MB below, in binary units like Explorer's folder properties, so the two can be
    /// compared.
    /// </summary>
    public static string Format(long bytes) =>
        bytes >= 1L << 30
            ? (bytes / (double)(1L << 30)).ToString("0.00", CultureInfo.InvariantCulture) + " GB"
            : (bytes / (double)(1L << 20)).ToString("0", CultureInfo.InvariantCulture) + " MB";

    /// <summary>The photos that aren't deleted, each counted once: album copies don't add to it.</summary>
    public static long Total(IEnumerable<Photo> photos) => photos.Where(p => !p.Deleted).Sum(p => p.Primary.Size);
}
