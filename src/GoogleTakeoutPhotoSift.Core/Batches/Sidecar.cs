using System.Globalization;
using System.Text.Json;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Batches;

/// <summary>What a photo's sidecar says: when it was taken, and whether it came in through partner sharing.</summary>
public readonly record struct SidecarFields(long? TakenUnix, bool FromPartnerSharing);

/// <summary>Reads the fields we need from a Takeout .json sidecar.</summary>
public static class Sidecar
{
    /// <summary>
    /// <c>photoTakenTime.timestamp</c> as unix seconds, or null when the file can't be read, the field
    /// is missing or it isn't positive. A port of <c>taken_json_time</c> in split_year.py.
    /// </summary>
    public static long? ReadTakenTime(string jsonPath) => Read(jsonPath).TakenUnix;

    /// <summary>
    /// The taken time (see <see cref="ReadTakenTime"/>) and whether
    /// <c>googlePhotosOrigin.fromPartnerSharing</c> is there, in one read.
    /// </summary>
    public static SidecarFields Read(string jsonPath)
    {
        try
        {
            using var stream = File.OpenRead(jsonPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return default;
            var partner = root.TryGetProperty("googlePhotosOrigin", out var origin)
                && origin.ValueKind == JsonValueKind.Object && origin.TryGetProperty("fromPartnerSharing", out _);
            return new SidecarFields(TakenTime(root), partner);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return default;
        }
    }

    private static long? TakenTime(JsonElement root)
    {
        if (!root.TryGetProperty("photoTakenTime", out var taken)
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
