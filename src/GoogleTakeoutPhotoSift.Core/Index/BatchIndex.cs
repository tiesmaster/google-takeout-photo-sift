using System.Text.RegularExpressions;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Batches;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

public sealed record IndexOptions
{
    /// <summary>
    /// Also treat the same album-only photo in several albums as one photo with copies (decision 3).
    /// Off reproduces rotate_viewer.py exactly, where each such file is its own photo.
    /// </summary>
    public bool GroupAlbumOnlyCopies { get; init; } = true;

    /// <summary>rotate_viewer.py's grouping, for comparing photo numbers with the Python viewer.</summary>
    public static IndexOptions Python { get; } = new() { GroupAlbumOnlyCopies = false };
}

/// <summary>
/// All photos of a batch with their copies, sidecars and albums. A port of <c>collect</c> in
/// rotate_viewer.py: photos are listed year folders first (in name order), then the album photos that
/// have no copy there.
/// </summary>
public sealed partial class BatchIndex
{
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp", ".heic", ".heif",
    };

    // Python sorts Windows paths by their lower-cased text.
    private static readonly IComparer<string> FolderOrder = Comparer<string>.Create(
        (a, b) => string.CompareOrdinal(a.ToLowerInvariant(), b.ToLowerInvariant()));

    [GeneratedRegex(@"^Photos from (\d{4})$")]
    private static partial Regex YearFolderRegex();

    private BatchIndex(string batchPath, List<Photo> photos, List<Album> albums,
        Dictionary<string, string?> sidecars, Dictionary<string, HashSet<string>> sidecarUsers,
        Dictionary<string, string> livePhotoVideos)
    {
        BatchPath = batchPath;
        Photos = photos;
        Albums = albums;
        Sidecars = sidecars;
        SidecarUsers = sidecarUsers;
        LivePhotoVideos = livePhotoVideos;
    }

    public string BatchPath { get; }

    public string PhotosRoot => BatchLocator.PhotosRoot(BatchPath);

    /// <summary>Photos in rotate_viewer.py order.</summary>
    public IReadOnlyList<Photo> Photos { get; }

    public IReadOnlyList<Album> Albums { get; }

    /// <summary>Every media file (videos included) → its sidecar path, or null.</summary>
    public IReadOnlyDictionary<string, string?> Sidecars { get; }

    /// <summary>
    /// Sidecar path → the media files that use it. An original and its <c>-edited</c> copy (or a video)
    /// can share one, which matters when deleting.
    /// </summary>
    public IReadOnlyDictionary<string, HashSet<string>> SidecarUsers { get; }

    /// <summary>
    /// Image path → the video of the same Live Photo: same name apart from the extension
    /// (<c>.mov</c>/<c>.mp4</c>), in the same folder. A delete moves it along (decision 10).
    /// </summary>
    public IReadOnlyDictionary<string, string> LivePhotoVideos { get; }

    public static readonly IReadOnlySet<string> LivePhotoVideoExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mov", ".mp4" };

    public static bool IsYearFolder(string folderName) => YearFolderRegex().IsMatch(folderName);

    public static BatchIndex Load(string batchPath, IndexOptions? options = null)
    {
        options ??= new IndexOptions();
        var root = BatchLocator.PhotosRoot(batchPath);
        var folders = Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>()
            .Order(FolderOrder).ToList();

        var sidecars = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var users = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var videos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scanned = folders.ToDictionary(f => f, f => ScanFolder(root, f, sidecars, users, videos));

        var yearPhotos = new List<List<MediaFile>>();
        var albumPhotos = new List<List<MediaFile>>();
        var byName = new Dictionary<string, List<MediaFile>>(StringComparer.OrdinalIgnoreCase);
        var albumOnlyByName = new Dictionary<string, List<MediaFile>>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders.Where(IsYearFolder))
        {
            foreach (var file in scanned[folder])
            {
                var group = new List<MediaFile> { file };
                yearPhotos.Add(group);
                byName.TryAdd(file.Name, group);
            }
        }

        var albums = new List<Album>();
        foreach (var folder in folders.Where(f => !IsYearFolder(f)))
        {
            var title = Sidecar.ReadAlbumTitle(Path.Combine(root, folder, SidecarMatcher.AlbumMetadata));
            albums.Add(new Album(folder, string.IsNullOrWhiteSpace(title) ? folder : title));
            foreach (var file in scanned[folder])
            {
                if (byName.TryGetValue(file.Name, out var group) && IsSamePhoto(group[0], file))
                {
                    group.Add(file);
                }
                else if (options.GroupAlbumOnlyCopies
                         && albumOnlyByName.TryGetValue(file.Name, out var albumGroup)
                         && IsSamePhoto(albumGroup[0], file))
                {
                    albumGroup.Add(file);
                }
                else
                {
                    var newGroup = new List<MediaFile> { file };
                    albumPhotos.Add(newGroup);
                    albumOnlyByName.TryAdd(file.Name, newGroup);
                }
            }
        }

        var photos = yearPhotos.Concat(albumPhotos).Select(g => new Photo(g)).ToList();
        return new BatchIndex(batchPath, photos, albums, sidecars, users, videos);
    }

    /// <summary>
    /// A same-named file is a copy when the sidecar dates are equal, or the bytes are identical.
    /// </summary>
    public static bool IsSamePhoto(MediaFile main, MediaFile other) =>
        (main.TakenUnix is not null && main.TakenUnix == other.TakenUnix) || FilesAreIdentical(main.Path, other.Path);

    public static bool FilesAreIdentical(string a, string b)
    {
        var infoA = new FileInfo(a);
        var infoB = new FileInfo(b);
        if (!infoA.Exists || !infoB.Exists || infoA.Length != infoB.Length)
            return false;

        const int bufferSize = 1 << 16;
        using var streamA = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize);
        using var streamB = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize);
        var bufferA = new byte[bufferSize];
        var bufferB = new byte[bufferSize];
        while (true)
        {
            var readA = streamA.ReadAtLeast(bufferA, bufferSize, throwOnEndOfStream: false);
            var readB = streamB.ReadAtLeast(bufferB, bufferSize, throwOnEndOfStream: false);
            if (readA != readB || !bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                return false;
            if (readA == 0)
                return true;
        }
    }

    // The images of one folder, sorted by name ignoring case. Records every media file's sidecar (videos
    // too) along the way.
    private static List<MediaFile> ScanFolder(string root, string folder,
        Dictionary<string, string?> sidecars, Dictionary<string, HashSet<string>> users,
        Dictionary<string, string> livePhotoVideos)
    {
        var dir = Path.Combine(root, folder);
        // The listing carries each file's length, so the sizes come for free.
        var sizes = new DirectoryInfo(dir).EnumerateFiles().ToDictionary(f => f.Name, f => f.Length, StringComparer.OrdinalIgnoreCase);
        var names = sizes.Keys.ToList();
        var images = new List<MediaFile>();
        foreach (var (name, json) in SidecarMatcher.Match(names)
                     .OrderBy(kv => SidecarMatcher.Fold(kv.Key), StringComparer.Ordinal))
        {
            var path = Path.Combine(dir, name);
            var jsonPath = json is null ? null : Path.Combine(dir, json);
            sidecars[path] = jsonPath;
            if (jsonPath is not null)
            {
                if (!users.TryGetValue(jsonPath, out var set))
                    users[jsonPath] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(path);
            }
            if (ImageExtensions.Contains(Path.GetExtension(name)))
                images.Add(new MediaFile(path, folder, jsonPath, jsonPath is null ? null : Sidecar.ReadTakenTime(jsonPath), sizes[name]));
        }
        // A video named like an image in this folder is that image's Live Photo video.
        var videoByStem = names.Where(n => LivePhotoVideoExtensions.Contains(Path.GetExtension(n)))
            .GroupBy(n => Path.GetFileNameWithoutExtension(n), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var image in images)
        {
            if (videoByStem.TryGetValue(Path.GetFileNameWithoutExtension(image.Name), out var video))
                livePhotoVideos[image.Path] = Path.Combine(dir, video);
        }
        return images;
    }
}
