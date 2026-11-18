using System.Text;
using System.Text.Json;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Support;

/// <summary>
/// A small batch tree in a temp folder (<c>&lt;batch&gt;\Takeout\Google Photos\...</c>), deleted on
/// dispose. File contents are tiny byte strings; the index never decodes them.
/// </summary>
public sealed class FakeBatch : IDisposable
{
    private readonly string _tempRoot;

    public FakeBatch(string name = "2016")
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "photo-sift-tests", Guid.NewGuid().ToString("N"));
        BatchPath = Path.Combine(_tempRoot, name);
        PhotosRoot = Path.Combine(BatchPath, "Takeout", "Google Photos");
        Directory.CreateDirectory(PhotosRoot);
    }

    public string BatchPath { get; }

    /// <summary>The folder holding the batch, standing in for the real batches folder.</summary>
    public string BatchesRoot => _tempRoot;

    public string DeletedRoot => Path.Combine(_tempRoot, "deleted", Path.GetFileName(BatchPath), "Takeout", "Google Photos");

    /// <summary>
    /// Every file under the batches root (the batch and the deleted tree) with its content hash and
    /// modified time, to compare before and after.
    /// </summary>
    public SortedDictionary<string, string> Snapshot()
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(_tempRoot, "*", SearchOption.AllDirectories))
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(file)));
            result[Path.GetRelativePath(_tempRoot, file).Replace(Path.DirectorySeparatorChar, '/')] =
                $"{hash[..12]} {System.IO.File.GetLastWriteTimeUtc(file):O}";
        }
        return result;
    }

    public string PhotosRoot { get; }

    public string YearFolder => "Photos from 2016";

    /// <summary>A photo in the year folder, with a standard sidecar unless <paramref name="json"/> says otherwise.</summary>
    public FakeBatch Year(string name, long? taken = null, string? content = null, string? json = "", bool noJson = false)
        => File(YearFolder, name, taken, content, json, noJson);

    /// <summary>
    /// A file in <paramref name="folder"/>. <paramref name="json"/>: "" for the standard
    /// <c>name.supplemental-metadata.json</c>, a name for a custom sidecar name, or use
    /// <paramref name="noJson"/> for none.
    /// </summary>
    public FakeBatch File(string folder, string name, long? taken = null, string? content = null,
        string? json = "", bool noJson = false)
    {
        var dir = Path.Combine(PhotosRoot, folder);
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllBytes(Path.Combine(dir, name), Encoding.UTF8.GetBytes(content ?? $"pixels of {name}"));
        if (!noJson)
        {
            var jsonName = string.IsNullOrEmpty(json) ? $"{name}.supplemental-metadata.json" : json;
            Sidecar(folder, jsonName, taken);
        }
        return this;
    }

    public FakeBatch Sidecar(string folder, string jsonName, long? taken)
    {
        var dir = Path.Combine(PhotosRoot, folder);
        Directory.CreateDirectory(dir);
        object body = taken is null
            ? new { title = jsonName }
            : new { title = jsonName, photoTakenTime = new { timestamp = taken.Value.ToString() } };
        System.IO.File.WriteAllText(Path.Combine(dir, jsonName), JsonSerializer.Serialize(body));
        return this;
    }

    public FakeBatch Album(string folder, string? title = null)
    {
        var dir = Path.Combine(PhotosRoot, folder);
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(Path.Combine(dir, "metadata.json"), JsonSerializer.Serialize(new { title = title ?? folder }));
        return this;
    }

    public string PathOf(string folder, string name) => Path.Combine(PhotosRoot, folder, name);

    public string Relative(string fullPath) => Path.GetRelativePath(PhotosRoot, fullPath).Replace('\\', '/');

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
