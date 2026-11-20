namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

/// <summary>An <c>-edited</c> copy that only differs a little from its original: drop <see cref="Edited"/>.</summary>
/// <param name="Difference">Mean difference per colour channel (0–255) of the two pictures as shown.</param>
public sealed record EditedPair(Photo Original, Photo Edited, double Difference);

/// <summary>An <c>-edited</c> copy that is left alone, and why.</summary>
public sealed record EditedSkip(Photo Edited, Photo? Original, string Reason);

public sealed record EditedReport(IReadOnlyList<EditedPair> Pairs, IReadOnlyList<EditedSkip> Skipped);

/// <summary>
/// Finds the <c>-edited</c> copies Google Photos, Picasa or an iPhone saved next to the original
/// (<c>IMG_1.JPG</c> → <c>IMG_1-edited.JPG</c>, <c>-bewerkt</c> in Dutch). Most are an automatic
/// enhance: the same picture a few percent brighter, re-encoded. Those can go, since the original has
/// the full quality. A copy whose shape changed (cropped, turned) or that looks clearly different (a
/// filter or effect) is a deliberate edit and is skipped, for the user to judge.
/// </summary>
public static class EditedCopies
{
    /// <summary>A mean difference from this many levels (out of 255) up is a deliberate edit.</summary>
    public const double MaxDifference = 25;

    /// <summary>Shown width / height may differ this much before the shape counts as changed.</summary>
    public const double MaxAspectDifference = 0.02;

    private static readonly string[] Suffixes = ["-edited", "-bewerkt"];

    /// <summary>The original's name without extension, when this is an edited copy's name.</summary>
    public static string? OriginalStem(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        return Suffixes.FirstOrDefault(s => stem.EndsWith(s, StringComparison.OrdinalIgnoreCase)) is { } suffix
            ? stem[..^suffix.Length]
            : null;
    }

    /// <param name="header">The stored size and orientation of a file, or null if it can't be read.</param>
    /// <param name="sample">
    /// The picture as shown (orientation applied), squeezed to a fixed small size, as colour bytes; or
    /// null if it can't be decoded. The same size for every file, so two samples compare byte by byte.
    /// </param>
    public static EditedReport Find(BatchIndex index, Func<string, ImageHeader?> header, Func<string, int, byte[]?> sample)
    {
        var byStem = index.Photos
            .Where(p => !p.Deleted && !p.IsVideo && OriginalStem(p.Primary.Name) is null)
            .ToLookup(p => (p.Primary.Folder.ToLowerInvariant(), Path.GetFileNameWithoutExtension(p.Primary.Name).ToLowerInvariant()));
        var pairs = new List<EditedPair>();
        var skipped = new List<EditedSkip>();
        foreach (var edited in index.Photos.Where(p => !p.Deleted && !p.IsVideo))
        {
            if (OriginalStem(edited.Primary.Name) is not { } stem)
                continue;
            var originals = byStem[(edited.Primary.Folder.ToLowerInvariant(), stem.ToLowerInvariant())].ToList();
            if (originals.Count != 1)
            {
                skipped.Add(new EditedSkip(edited, null, originals.Count == 0 ? "no original next to it" : "several files could be the original"));
                continue;
            }
            var original = originals[0];
            var reason = Check(original, edited, header, sample, out var difference);
            if (reason is null)
                pairs.Add(new EditedPair(original, edited, difference));
            else
                skipped.Add(new EditedSkip(edited, original, reason));
        }
        return new EditedReport(pairs, skipped);
    }

    /// <summary>Mean absolute difference per byte of two samples of the same size.</summary>
    public static double MeanDifference(byte[] a, byte[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            throw new ArgumentException("samples differ in size");
        long sum = 0;
        for (var i = 0; i < a.Length; i++)
            sum += Math.Abs(a[i] - b[i]);
        return (double)sum / a.Length;
    }

    private static string? Check(Photo original, Photo edited, Func<string, ImageHeader?> header,
        Func<string, int, byte[]?> sample, out double difference)
    {
        difference = 0;
        // Dropping the edited copy takes its album copies along; the original must be in those albums too.
        var missing = AlbumFolders(edited).Except(AlbumFolders(original), StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0)
            return $"only the edited one is in {string.Join(", ", missing)}";

        if (header(original.Primary.Path) is not { } ho || header(edited.Primary.Path) is not { } he)
            return "can't read one of the files";
        var (ra, rb) = (ShownAspect(ho), ShownAspect(he));
        if (Math.Abs(ra - rb) > MaxAspectDifference)
            return "shape changed (cropped or turned): look at it yourself";

        if (sample(original.Primary.Path, ho.Orientation) is not { } so || sample(edited.Primary.Path, he.Orientation) is not { } se)
            return "can't decode one of the files";
        difference = MeanDifference(so, se);
        return difference >= MaxDifference
            ? $"strongly changed ({difference:0}/255, a filter or effect): look at it yourself"
            : null;
    }

    private static double ShownAspect(ImageHeader h) =>
        h.Orientation is >= 5 and <= 8 ? (double)h.Height / h.Width : (double)h.Width / h.Height;

    private static HashSet<string> AlbumFolders(Photo photo) =>
        photo.Files.Select(f => f.Folder).Where(f => !BatchIndex.IsYearFolder(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
