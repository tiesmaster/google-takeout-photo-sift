using System.Text.Json;
using System.Text.Json.Serialization;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Batches;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Settings;

/// <summary>
/// Optional settings from <c>%LOCALAPPDATA%\GoogleTakeoutPhotoSift\settings.json</c>. Every field has a
/// default, so the file only needs the ones you change.
/// </summary>
public sealed record SiftSettings
{
    public string BatchesRoot { get; init; } = BatchLocator.DefaultBatchesRoot;

    public string ExifToolPath { get; init; } = "exiftool";

    /// <summary>Sharp frames decoded ahead of the current photo, in the direction of travel.</summary>
    public int PrefetchAhead { get; init; } = 4;

    /// <summary>Sharp frames kept decoded behind the current photo.</summary>
    public int PrefetchBehind { get; init; } = 2;

    /// <summary>Memory budget for decoded frames.</summary>
    public int CacheMegabytes { get; init; } = 512;

    /// <summary>Memory budget for file contents read ahead of decoding.</summary>
    public int FileCacheMegabytes { get; init; } = 150;

    public bool ShowTiming { get; init; }

    public bool ShowStrip { get; init; } = true;

    /// <summary>The album list on the left.</summary>
    public bool ShowAlbumList { get; init; } = true;

    /// <summary>Thumbnails on each side of the current photo in the strip.</summary>
    public int StripPerSide { get; init; } = 5;

    public static string AppDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoogleTakeoutPhotoSift");

    public static string DefaultPath => Path.Combine(AppDataFolder, "settings.json");

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The settings in <paramref name="path"/>, or the defaults when it's missing or unreadable.</summary>
    public static SiftSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SiftSettings>(File.ReadAllText(path), Json) ?? new SiftSettings()
                : new SiftSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new SiftSettings();
        }
    }
}
