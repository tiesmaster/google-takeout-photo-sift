using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Batches;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

/// <summary>
/// A run of photos reviewed together: one album, the loose photos (no album), or the loose photos
/// received through WhatsApp.
/// </summary>
public sealed class Section(Album? album, IReadOnlyList<Photo> photos, int start, bool isWhatsApp = false)
{
    /// <summary>Null for the loose photos and the WhatsApp photos.</summary>
    public Album? Album { get; } = album;

    public bool IsWhatsApp { get; } = isWhatsApp;

    public bool IsLoose => Album is null && !IsWhatsApp;

    public IReadOnlyList<Photo> Photos { get; } = photos;

    /// <summary>Index of the first photo in <see cref="Review.Photos"/>.</summary>
    public int Start { get; } = start;

    public int End => Start + Photos.Count;

    public string Title => IsWhatsApp ? "WhatsApp" : Album?.Title ?? "Loose photos";
}

/// <summary>An album as the album picker lists it.</summary>
public sealed record AlbumEntry(Album Album, Section? Section, int MemberCount, DateTimeOffset? FirstTaken, Album? ShownIn);

/// <summary>
/// The order photos are reviewed in. Album mode: albums first, ordered by the date of their first
/// photo (decision 1); each photo appears once, in the first album it belongs to (decision 2), with
/// the others as "also in"; then the loose photos (decision 11). Within a section by date taken, then
/// file name. Loose photos received through WhatsApp come last, in a section of their own (unless
/// <c>groupWhatsApp</c> is off); those in an album stay there. Python mode keeps rotate_viewer.py's order in one section, for comparing numbers.
/// </summary>
public sealed class Review
{
    private readonly Dictionary<Photo, (int Section, IReadOnlyList<Album> Albums)> _byPhoto;

    private Review(BatchIndex index, List<Photo> photos, List<Section> sections, List<AlbumEntry> albums,
        Dictionary<Photo, (int, IReadOnlyList<Album>)> byPhoto, bool isPython)
    {
        Index = index;
        Photos = photos;
        Sections = sections;
        Albums = albums;
        _byPhoto = byPhoto;
        IsPython = isPython;
    }

    public BatchIndex Index { get; }

    /// <summary>All photos, section after section.</summary>
    public IReadOnlyList<Photo> Photos { get; }

    /// <summary>The album sections in order, then the loose photos and the WhatsApp photos (if any).</summary>
    public IReadOnlyList<Section> Sections { get; }

    /// <summary>Every album with media in this batch, in review order; also those whose photos all show elsewhere.</summary>
    public IReadOnlyList<AlbumEntry> Albums { get; }

    public bool IsPython { get; }

    /// <summary>The number of album sections (the loose photos aren't an album).</summary>
    public int AlbumCount => Sections.Count(s => s.Album is not null);

    /// <summary>Index of the loose photos' section, or -1.</summary>
    public int LooseSection => Sections.Select((s, i) => (s, i)).FirstOrDefault(x => x.s.IsLoose && !IsPython, (null!, -1)).Item2;

    /// <summary>Index of the WhatsApp section, or -1 (also when they're kept between the other loose photos).</summary>
    public int WhatsAppSection => Sections.Select((s, i) => (s, i)).FirstOrDefault(x => x.s.IsWhatsApp, (null!, -1)).Item2;

    public int SectionIndexOf(Photo photo) => _byPhoto[photo].Section;

    /// <summary>The albums a photo is in: its home album first, then the others ("also in").</summary>
    public IReadOnlyList<Album> AlbumsOf(Photo photo) => _byPhoto[photo].Albums;

    /// <summary>rotate_viewer.py's order, as one section.</summary>
    public static Review Python(BatchIndex index)
    {
        var photos = index.Photos.ToList();
        var section = new Section(null, photos, 0);
        var albumsByFolder = index.Albums.ToDictionary(a => a.Folder, StringComparer.OrdinalIgnoreCase);
        var byPhoto = photos.ToDictionary(p => p, p => (0, (IReadOnlyList<Album>)AlbumsIn(p, albumsByFolder)));
        return new Review(index, photos, [section], [], byPhoto, isPython: true);
    }

    /// <summary>
    /// The album-first order. <paramref name="exifDate"/> supplies a date for photos without a sidecar
    /// date (EXIF DateTimeOriginal by default); it's only called for those.
    /// </summary>
    public static Review AlbumFirst(BatchIndex index, Func<string, long?>? exifDate = null, bool groupWhatsApp = true)
    {
        exifDate ??= ReadExifDate;
        var albumsByFolder = index.Albums.ToDictionary(a => a.Folder, StringComparer.OrdinalIgnoreCase);
        var taken = index.Photos.ToDictionary(p => p, p => TakenOf(p, exifDate));
        var membership = index.Photos.ToDictionary(p => p, p => AlbumsIn(p, albumsByFolder));

        // Albums by the date of their first photo (all their photos, also those shown elsewhere).
        var members = index.Albums.ToDictionary(a => a, _ => new List<Photo>());
        foreach (var (photo, albums) in membership)
        {
            foreach (var album in albums)
                members[album].Add(photo);
        }
        var albumOrder = members
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => (Album: kv.Key, First: kv.Value.Select(p => taken[p]).Where(t => t is not null).Min()))
            .OrderBy(a => a.First is null)
            .ThenBy(a => a.First)
            .ThenBy(a => a.Album.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Album.Folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rank = albumOrder.Select((a, i) => (a.Album, i)).ToDictionary(x => x.Album, x => x.i);

        // Each photo's albums in review order; the first is its home.
        var sorted = membership.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Album>)kv.Value.OrderBy(a => rank[a]).ToList());
        var home = index.Photos.GroupBy(p => sorted[p].Count > 0 ? sorted[p][0] : null).ToDictionary(g => g.Key ?? Loose, g => g.ToList());

        var photos = new List<Photo>();
        var sections = new List<Section>();
        var byPhoto = new Dictionary<Photo, (int, IReadOnlyList<Album>)>();
        var entries = new List<AlbumEntry>();
        void Add(Album? album, List<Photo> own, bool isWhatsApp = false)
        {
            var ordered = own.OrderBy(p => taken[p] is null).ThenBy(p => taken[p])
                .ThenBy(p => SidecarMatcher.Fold(p.Primary.Name), StringComparer.Ordinal)
                .ThenBy(p => p.Primary.Folder, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var section = new Section(album, ordered, photos.Count, isWhatsApp);
            foreach (var photo in ordered)
                byPhoto[photo] = (sections.Count, sorted[photo]);
            sections.Add(section);
            photos.AddRange(ordered);
        }
        foreach (var (album, first) in albumOrder)
        {
            if (home.TryGetValue(album, out var own))
                Add(album, own);
        }
        if (home.TryGetValue(Loose, out var loose))
        {
            var whatsApp = groupWhatsApp ? loose.Where(p => p.IsWhatsApp).ToList() : [];
            if (whatsApp.Count < loose.Count)
                Add(null, loose.Except(whatsApp).ToList());
            if (whatsApp.Count > 0)
                Add(null, whatsApp, isWhatsApp: true);
        }

        foreach (var (album, first) in albumOrder)
        {
            var section = sections.FirstOrDefault(s => s.Album == album);
            var shownIn = section is null ? sorted[members[album][0]][0] : null;
            entries.Add(new AlbumEntry(album, section, members[album].Count,
                first is long t ? DateTimeOffset.FromUnixTimeSeconds(t) : null, shownIn));
        }
        return new Review(index, photos, sections, entries, byPhoto, isPython: false);
    }

    // Stands in for "no album" as a dictionary key.
    private static readonly Album Loose = new("", "");

    private static List<Album> AlbumsIn(Photo photo, Dictionary<string, Album> albumsByFolder) =>
        photo.Files.Select(f => albumsByFolder.GetValueOrDefault(f.Folder)).OfType<Album>().Distinct().ToList();

    private static long? TakenOf(Photo photo, Func<string, long?> exifDate) =>
        photo.Files.Select(f => f.TakenUnix).FirstOrDefault(t => t is not null) ?? exifDate(photo.Primary.Path);

    /// <summary>EXIF DateTimeOriginal as unix seconds, read as UTC like the sidecar dates sort; null if absent.</summary>
    public static long? ReadExifDate(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            return exif is not null && exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var date)
                ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)).ToUnixTimeSeconds()
                : null;
        }
        catch (Exception e) when (e is ImageProcessingException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
