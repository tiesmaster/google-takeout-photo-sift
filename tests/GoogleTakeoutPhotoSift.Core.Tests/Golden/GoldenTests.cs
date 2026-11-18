using System.Text.Json;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Batches;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Golden;

/// <summary>
/// Compares the C# port with what the Python viewer does on real batches, exported by
/// tools/export_golden.py to local/golden (git-ignored). Skipped when there's no export, as in CI.
/// </summary>
public class GoldenTests
{
    private static readonly string? GoldenDir = FindGoldenDir();

    private sealed record GoldenFolder(List<string> Names, Dictionary<string, string?> Sidecars);

    private sealed record GoldenBatch(string Batch, Dictionary<string, GoldenFolder> Folders, List<List<string>> Photos);

    [Fact]
    public void Sidecar_matching_matches_python()
    {
        var files = GoldenFiles();
        var mismatches = new List<string>();
        foreach (var (file, golden) in files)
        {
            foreach (var (folder, expected) in golden.Folders)
            {
                var actual = SidecarMatcher.Match(expected.Names);
                foreach (var (media, json) in expected.Sidecars)
                {
                    if (!actual.TryGetValue(media, out var got) || got != json)
                        mismatches.Add($"{file}: {folder}/{media}: python {json ?? "-"}, C# {got ?? "-"}");
                }
                if (actual.Count != expected.Sidecars.Count)
                    mismatches.Add($"{file}: {folder}: python {expected.Sidecars.Count} media, C# {actual.Count}");
            }
        }
        Assert.True(mismatches.Count == 0, Report(mismatches));
    }

    [Fact]
    public void Photo_groups_and_order_match_python()
    {
        var files = GoldenFiles();
        var mismatches = new List<string>();
        foreach (var (file, golden) in files)
        {
            if (!Directory.Exists(golden.Batch))
                continue;
            var index = BatchIndex.Load(golden.Batch, IndexOptions.Python);
            var actual = index.Photos
                .Select(p => p.Files.Select(f => Path.GetRelativePath(index.PhotosRoot, f.Path).Replace('\\', '/')).ToList())
                .ToList();
            if (actual.Count != golden.Photos.Count)
                mismatches.Add($"{file}: python {golden.Photos.Count} photos, C# {actual.Count}");
            for (var i = 0; i < Math.Min(actual.Count, golden.Photos.Count); i++)
            {
                if (!actual[i].SequenceEqual(golden.Photos[i]))
                {
                    mismatches.Add($"{file}: photo {i + 1}: python [{string.Join(", ", golden.Photos[i])}], C# [{string.Join(", ", actual[i])}]");
                }
            }
        }
        Assert.True(mismatches.Count == 0, Report(mismatches));
    }

    [Fact]
    public void Orientation_matches_exiftool()
    {
        var path = GoldenDir is null ? null : Path.Combine(Path.GetDirectoryName(GoldenDir)!, "orientations.json");
        Assert.SkipWhen(path is null || !File.Exists(path), "No local/orientations.json; run tools/export_golden.py");
        var expected = JsonSerializer.Deserialize<Dictionary<string, int?>>(File.ReadAllText(path!))!;
        var mismatches = new List<string>();
        var checkedCount = 0;
        foreach (var (image, orientation) in expected)
        {
            if (!File.Exists(image))
                continue;
            checkedCount++;
            var actual = OrientationReader.Read(image);
            // Values outside 1-8 (some cameras write 0) count as upright, so they can still be rotated.
            var normalized = orientation is >= 1 and <= 8 ? orientation.Value : 1;
            if (actual != normalized)
                mismatches.Add($"{image}: exiftool {orientation?.ToString() ?? "-"}, C# {actual}");
        }
        Assert.True(checkedCount > 0, "None of the images in orientations.json exist any more");
        Assert.True(mismatches.Count == 0, Report(mismatches));
    }

    private static List<(string File, GoldenBatch Golden)> GoldenFiles()
    {
        Assert.SkipWhen(GoldenDir is null, "No local/golden export; run tools/export_golden.py");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var files = Directory.GetFiles(GoldenDir!, "*.json")
            .Select(f => (Path.GetFileName(f), JsonSerializer.Deserialize<GoldenBatch>(File.ReadAllText(f), options)!))
            .ToList();
        Assert.SkipWhen(files.Count == 0, "local/golden is empty; run tools/export_golden.py");
        return files;
    }

    private static string Report(List<string> mismatches) =>
        $"{mismatches.Count} differences with Python (re-export if the batch changed):\n"
        + string.Join("\n", mismatches.Take(30));

    private static string? FindGoldenDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GoogleTakeoutPhotoSift.slnx")))
            {
                var golden = Path.Combine(dir.FullName, "local", "golden");
                return Directory.Exists(golden) ? golden : null;
            }
        }
        return null;
    }
}
