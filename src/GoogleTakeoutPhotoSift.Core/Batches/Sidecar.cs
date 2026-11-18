using System.Globalization;
using System.Text.Json;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Batches;

/// <summary>Reads the fields we need from a Takeout .json sidecar.</summary>
public static class Sidecar
{
    /// <summary>
    /// <c>photoTakenTime.timestamp</c> as unix seconds, or null when the file can't be read, the field
    /// is missing or it isn't positive. A port of <c>taken_json_time</c> in split_year.py.
    /// </summary>
    public static long? ReadTakenTime(string jsonPath)
    {
        try
        {
            using var stream = File.OpenRead(jsonPath);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("photoTakenTime", out var taken)
                || taken.ValueKind != JsonValueKind.Object
                || !taken.TryGetProperty("timestamp", out var timestamp))
            {
                return null;
            }
            long? ts = timestamp.ValueKind switch
            {
                JsonValueKind.Number when timestamp.TryGetInt64(out var v) => v,
                JsonValueKind.String when long.TryParse(timestamp.GetString()?.Trim(), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var v) => v,
                _ => null,
            };
            return ts > 0 ? ts : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>An album folder's title from its metadata.json, or null.</summary>
    public static string? ReadAlbumTitle(string metadataPath)
    {
        try
        {
            using var stream = File.OpenRead(metadataPath);
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                ? title.GetString()
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
