using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.WebP;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

/// <summary>What a file says about where it came from: camera make, EXIF date and stored pixel size.</summary>
public readonly record struct ImageTraits(string? Make, bool HasExifDate, int Width, int Height);

/// <summary>
/// Recognises photos received through WhatsApp. WhatsApp strips the camera data and shrinks the
/// picture, and on an iPhone a saved one is just <c>IMG_1234.JPG</c>, so the name doesn't tell. What
/// does: no camera make, no EXIF date, and a long side of at most 1600 px (800 in 2014, 1280 or 1600
/// from 2015). Camera photos always have a make; own photos that lost their EXIF are full size.
/// </summary>
public static class WhatsAppPhotos
{
    public const int MaxLongSide = 1600;

    public static bool Matches(ImageTraits traits) =>
        string.IsNullOrWhiteSpace(traits.Make) && !traits.HasExifDate
        && traits.Width > 0 && traits.Height > 0 && Math.Max(traits.Width, traits.Height) <= MaxLongSide;

    /// <summary>Sets <see cref="Photo.IsWhatsApp"/> on every photo, by its primary file.</summary>
    public static void Mark(BatchIndex index, Func<string, ImageTraits?>? read = null)
    {
        read ??= ReadTraits;
        Parallel.ForEach(index.Photos, photo =>
            photo.IsWhatsApp = !photo.IsVideo && read(photo.Primary.Path) is { } traits && Matches(traits));
    }

    /// <summary>The file's traits from its headers only, or null if it can't be read.</summary>
    public static ImageTraits? ReadTraits(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            var make = directories.OfType<ExifIfd0Directory>().FirstOrDefault()?.GetString(ExifDirectoryBase.TagMake);
            var hasDate = directories.OfType<ExifSubIfdDirectory>().Any(d => d.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal));
            var (width, height) = Size(directories);
            return new ImageTraits(make, hasDate, width, height);
        }
        catch (Exception e) when (e is ImageProcessingException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (int Width, int Height) Size(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        foreach (var directory in directories)
        {
            var (w, h) = directory switch
            {
                JpegDirectory => (JpegDirectory.TagImageWidth, JpegDirectory.TagImageHeight),
                PngDirectory => (PngDirectory.TagImageWidth, PngDirectory.TagImageHeight),
                WebPDirectory => (WebPDirectory.TagImageWidth, WebPDirectory.TagImageHeight),
                _ => (-1, -1),
            };
            if (w >= 0 && directory.TryGetInt32(w, out var width) && directory.TryGetInt32(h, out var height))
                return (width, height);
        }
        return (0, 0);
    }
}
