using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;

/// <summary>What the info panel shows from a file's own metadata.</summary>
public sealed record PhotoInfo(string? Make, string? Model, DateTime? DateTimeOriginal, int? Width, int? Height)
{
    public static readonly PhotoInfo Empty = new(null, null, null, null, null);

    /// <summary>"Canon EOS 600D"; just the model when it already names the make ("HTC Desire S", not "HTC HTC Desire S").</summary>
    public string? Camera =>
        string.IsNullOrWhiteSpace(Model) ? (string.IsNullOrWhiteSpace(Make) ? null : Make)
        : string.IsNullOrWhiteSpace(Make) || Model.StartsWith(Make, StringComparison.OrdinalIgnoreCase) ? Model
        : $"{Make} {Model}";

    public static PhotoInfo Read(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var sub = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            DateTime? taken = sub is not null && sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var d) ? d : null;
            int? width = null, height = null;
            foreach (var directory in directories)
            {
                foreach (var tag in directory.Tags)
                {
                    // Image Width/Height under whatever name the format uses (JPEG, PNG, HEIF, EXIF).
                    if (width is null && tag.Name is "Image Width" or "Exif Image Width" && directory.TryGetInt32(tag.Type, out var w) && w > 0)
                        width = w;
                    if (height is null && tag.Name is "Image Height" or "Exif Image Height" && directory.TryGetInt32(tag.Type, out var h) && h > 0)
                        height = h;
                }
            }
            return new PhotoInfo(
                ifd0?.GetDescription(ExifDirectoryBase.TagMake)?.Trim(),
                ifd0?.GetDescription(ExifDirectoryBase.TagModel)?.Trim(),
                taken, width, height);
        }
        catch (Exception e) when (e is ImageProcessingException or IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    public static string OrientationName(int orientation) => orientation switch
    {
        1 => "upright",
        2 => "mirrored",
        3 => "upside down",
        4 => "mirrored, upside down",
        5 => "mirrored, turned left",
        6 => "turned right",
        7 => "mirrored, turned right",
        8 => "turned left",
        _ => "unknown",
    };
}
