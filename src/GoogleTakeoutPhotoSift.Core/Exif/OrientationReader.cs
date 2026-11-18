using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;

/// <summary>
/// Reads the EXIF Orientation tag from the file itself. For HEIC this is the tag in the Exif item, not
/// what a decoder reports after applying the HEIF container's own rotation.
/// </summary>
public static class OrientationReader
{
    /// <summary>The orientation (1–8), or 1 when there's no valid tag or the metadata can't be read.</summary>
    public static int Read(Stream stream)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(stream);
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            return ifd0 is not null && ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation)
                && orientation is >= 1 and <= 8
                ? orientation
                : 1;
        }
        catch (Exception e) when (e is ImageProcessingException or IOException)
        {
            return 1;
        }
    }

    public static int Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return Read(stream);
    }

    public static int Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Read(stream);
    }
}
