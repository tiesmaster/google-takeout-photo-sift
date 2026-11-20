using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Avi;
using MetadataExtractor.Formats.QuickTime;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

/// <summary>A video's length and frame size from its headers (.mov/.mp4 and .avi); unknown parts are null.</summary>
public sealed record VideoInfo(TimeSpan? Duration, int? Width, int? Height)
{
    public static VideoInfo Read(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            if (directories.OfType<AviDirectory>().FirstOrDefault() is { } avi)
            {
                return new VideoInfo(
                    TimeSpan.TryParse(avi.GetString(AviDirectory.TagDuration), CultureInfo.InvariantCulture, out var length) ? length : null,
                    avi.TryGetInt32(AviDirectory.TagWidth, out var w) ? w : null,
                    avi.TryGetInt32(AviDirectory.TagHeight, out var h) ? h : null);
            }
            var duration = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault()
                ?.GetObject(QuickTimeMovieHeaderDirectory.TagDuration) as TimeSpan?;
            // The video track is the one with a frame size; the sound track has 0 × 0.
            var track = directories.OfType<QuickTimeTrackHeaderDirectory>()
                .FirstOrDefault(d => d.TryGetInt32(QuickTimeTrackHeaderDirectory.TagWidth, out var w) && w > 0);
            return new VideoInfo(duration,
                track?.TryGetInt32(QuickTimeTrackHeaderDirectory.TagWidth, out var width) == true ? width : null,
                track?.TryGetInt32(QuickTimeTrackHeaderDirectory.TagHeight, out var height) == true ? height : null);
        }
        catch (Exception e) when (e is ImageProcessingException or IOException or UnauthorizedAccessException)
        {
            return new VideoInfo(null, null, null);
        }
    }

    /// <summary><c>0:36</c>, <c>22:16</c>, <c>1:05:00</c>.</summary>
    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
