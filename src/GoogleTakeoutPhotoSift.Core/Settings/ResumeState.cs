using System.Text.Json;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Settings;

/// <summary>
/// The last position per batch (<c>%LOCALAPPDATA%\GoogleTakeoutPhotoSift\state.json</c>), so a restart
/// (dotnet watch, or another day) opens where you left off.
/// </summary>
public sealed class ResumeState
{
    /// <summary>A position: the photo's primary file relative to the batch, plus its number as a fallback.</summary>
    public sealed record Position(string RelativePath, int Index);

    public Dictionary<string, Position> Batches { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static string DefaultPath => Path.Combine(SiftSettings.AppDataFolder, "state.json");

    public static ResumeState Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path))
                return new ResumeState();
            var state = JsonSerializer.Deserialize<ResumeState>(File.ReadAllText(path), SiftSettings.Json);
            return state is null
                ? new ResumeState()
                : new ResumeState { Batches = new(state.Batches, StringComparer.OrdinalIgnoreCase) };
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ResumeState();
        }
    }

    /// <summary>Writes atomically (temp file, then replace), so a crash never leaves half a file.</summary>
    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, SiftSettings.Json));
        File.Move(temp, path, overwrite: true);
    }
}
