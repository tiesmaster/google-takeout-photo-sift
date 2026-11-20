using System.Numerics;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

/// <summary>The stored size and EXIF orientation of a file.</summary>
public readonly record struct ImageHeader(int Width, int Height, int Orientation);

/// <summary>A photo and its re-encoded twin: keep <see cref="Keep"/>, remove <see cref="Remove"/>.</summary>
public sealed record TwinPair(Photo Keep, Photo Remove, ImageHeader KeepHeader, ImageHeader RemoveHeader, int Distance);

/// <summary>Photos taken at the same second that aren't removed, and why.</summary>
public sealed record TwinSkip(IReadOnlyList<Photo> Photos, string Reason);

public sealed record TwinReport(IReadOnlyList<TwinPair> Pairs, IReadOnlyList<TwinSkip> Skipped);

/// <summary>
/// Finds photos that are in the batch twice under different names: the camera original (stored sensor
/// pixels plus an EXIF rotation of 3, 6 or 8) and a copy some app turned upright and saved again
/// (orientation 1). Only clear cases count as twins; everything else at the same second is reported
/// as skipped.
/// </summary>
public static class Twins
{
    /// <summary>Fingerprints this many bits apart (out of 64) still count as the same picture.</summary>
    public const int MaxDistance = 10;

    /// <param name="header">The stored size and orientation of a file, or null if it can't be read.</param>
    /// <param name="fingerprint">A 64-bit hash of the picture as shown (orientation applied), or null.</param>
    public static TwinReport Find(BatchIndex index, Func<string, ImageHeader?> header, Func<string, int, ulong?> fingerprint)
    {
        var pairs = new List<TwinPair>();
        var skipped = new List<TwinSkip>();
        var groups = index.Photos
            .Where(p => p.Primary.TakenUnix is not null && !p.IsVideo)
            .GroupBy(p => p.Primary.TakenUnix!.Value)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key);
        foreach (var group in groups)
        {
            var photos = group.OrderBy(p => p.Primary.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (photos.Count > 2)
            {
                skipped.Add(new TwinSkip(photos, $"{photos.Count} photos at the same second (a burst?)"));
                continue;
            }
            if (Check(photos[0], photos[1], header, fingerprint) is { } result)
            {
                if (result.Pair is { } pair)
                    pairs.Add(pair);
                else
                    skipped.Add(new TwinSkip(photos, result.Reason!));
            }
        }
        return new TwinReport(pairs, skipped);
    }

    private static (TwinPair? Pair, string? Reason)? Check(Photo a, Photo b, Func<string, ImageHeader?> header,
        Func<string, int, ulong?> fingerprint)
    {
        if (header(a.Primary.Path) is not { } ha || header(b.Primary.Path) is not { } hb)
            return (null, "can't read one of the files");
        var (original, copy, ho, hc) = (ha.Orientation, hb.Orientation) switch
        {
            (1, 3 or 6 or 8) => (b, a, hb, ha),
            (3 or 6 or 8, 1) => (a, b, ha, hb),
            _ => (null!, null!, default, default),
        };
        if (original is null)
            return (null, $"orientations {ha.Orientation} and {hb.Orientation}: not an original and an upright copy");

        // The copy's pixels must be the original's, turned as its orientation says.
        var turned = ho.Orientation is 6 or 8 ? (ho.Height, ho.Width) : (ho.Width, ho.Height);
        if ((hc.Width, hc.Height) != turned)
            return (null, $"sizes {ho.Width}×{ho.Height} (orientation {ho.Orientation}) and {hc.Width}×{hc.Height} don't match");

        var albumsOriginal = AlbumFolders(original);
        var albumsCopy = AlbumFolders(copy);
        if (!albumsOriginal.SetEquals(albumsCopy))
            return (null, $"in different albums ({string.Join(", ", albumsOriginal)} / {string.Join(", ", albumsCopy)})");

        if (fingerprint(original.Primary.Path, ho.Orientation) is not { } fo || fingerprint(copy.Primary.Path, 1) is not { } fc)
            return (null, "can't fingerprint one of the files");
        var distance = BitOperations.PopCount(fo ^ fc);
        if (distance > MaxDistance)
            return (null, $"different pictures (fingerprints {distance} bits apart)");

        return (new TwinPair(original, copy, ho, hc, distance), null);
    }

    private static HashSet<string> AlbumFolders(Photo photo) =>
        photo.Files.Select(f => f.Folder).Where(f => !BatchIndex.IsYearFolder(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
