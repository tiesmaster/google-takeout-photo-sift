using System.Text.RegularExpressions;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Batches;

/// <summary>
/// Finds each media file's .json sidecar in a Takeout folder, following Takeout's quirks: cut-off
/// names (<c>IMG.jpg.supplemental-me.json</c>), <c>(n)</c> numbering (<c>IMG(1).jpg</c> ↔
/// <c>IMG.jpg.supplemental-metadata(1).json</c>) and <c>-edited</c> copies sharing the original's
/// sidecar. A port of <c>match_sidecars</c> in split_year.py.
/// </summary>
public static partial class SidecarMatcher
{
    public const string AlbumMetadata = "metadata.json";

    private const string Supplemental = "supplemental-metadata";

    // Takeout localizes the "-edited" suffix to the account language.
    private static readonly string[] EditedSuffixes = ["-edited", "-bewerkt"];

    [GeneratedRegex(@"^(.*)\((\d+)\)$", RegexOptions.Singleline)]
    private static partial Regex DupRegex();

    public static bool IsJson(string name) => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public static bool IsSidecar(string name) => IsJson(name) && name != AlbumMetadata;

    /// <summary>
    /// Maps each media file name in <paramref name="names"/> (all file names of one folder, in listing
    /// order) to its sidecar name, or null. Every non-.json name gets an entry.
    /// </summary>
    public static Dictionary<string, string?> Match(IReadOnlyList<string> names)
    {
        var byN = new Dictionary<int, List<(string Prefix, string Name)>>();
        foreach (var name in names)
        {
            if (!IsSidecar(name))
                continue;
            var (prefix, n) = ParseJsonName(name);
            var key = n ?? -1;
            if (!byN.TryGetValue(key, out var list))
                byN[key] = list = [];
            list.Add((prefix, name));
        }

        var result = new Dictionary<string, string?>();
        foreach (var name in names)
        {
            if (IsJson(name))
                continue;
            (string Prefix, string Name)? best = null;
            foreach (var (target, n) in MediaKeys(name))
            {
                if (byN.TryGetValue(n ?? -1, out var candidates))
                {
                    foreach (var (prefix, jsonName) in candidates)
                    {
                        if (prefix.Length > 0 && target.StartsWith(prefix, StringComparison.Ordinal)
                            && (best is null || prefix.Length > best.Value.Prefix.Length))
                        {
                            best = (prefix, jsonName);
                        }
                    }
                }
                if (best is not null)
                    break;
            }
            result[name] = best?.Name;
        }
        return result;
    }

    /// <summary>
    /// <c>IMG_1.jpg.supplemental-metadata(1).json</c> → (<c>img_1.jpg</c>, 1). Takeout truncates long
    /// sidecar names, so the prefix may be a shortened media name, and the supplemental part may be cut
    /// off anywhere.
    /// </summary>
    internal static (string Prefix, int? N) ParseJsonName(string name)
    {
        var baseName = name[..^5];
        int? n = null;
        var m = DupRegex().Match(baseName);
        if (m.Success)
        {
            baseName = m.Groups[1].Value;
            n = int.Parse(m.Groups[2].Value);
        }
        var dot = baseName.LastIndexOf('.');
        if (dot >= 0)
        {
            var tail = baseName[(dot + 1)..];
            if (tail.Length > 0 && Supplemental.StartsWith(tail.ToLowerInvariant(), StringComparison.Ordinal))
                baseName = baseName[..dot];
        }
        return (Fold(baseName), n);
    }

    /// <summary>Candidate (prefix target, dup number) pairs to look up a media file's sidecar.</summary>
    internal static List<(string Target, int? N)> MediaKeys(string name)
    {
        var (stem, ext) = SplitExtension(name);
        int? n = null;
        var m = DupRegex().Match(stem);
        if (m.Success)
        {
            stem = m.Groups[1].Value;
            n = int.Parse(m.Groups[2].Value);
        }
        foreach (var suffix in EditedSuffixes)
        {
            if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                stem = stem[..^suffix.Length];
                break;
            }
        }
        var full = Fold(stem + ext);
        var keys = new List<(string, int?)> { (full, n), (Fold(name), null) };
        if (n is not null)
            keys.Add((full, null));
        return keys;
    }

    /// <summary>Python's <c>os.path.splitext</c>: leading dots don't start an extension.</summary>
    internal static (string Stem, string Extension) SplitExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || name[..dot].TrimStart('.').Length == 0)
            return (name, "");
        return (name[..dot], name[dot..]);
    }

    // Stands in for Python's str.casefold(); the two differ only for a few letters such as ß.
    internal static string Fold(string s) => s.ToLowerInvariant();
}
