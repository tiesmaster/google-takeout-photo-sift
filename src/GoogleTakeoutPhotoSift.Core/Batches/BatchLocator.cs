namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Batches;

/// <summary>Finds a batch folder from a year or a path, like <c>find_batch</c> in sync_album_copies.py.</summary>
public static class BatchLocator
{
    public const string DefaultBatchesRoot = @"C:\ties\takeout\batches";

    /// <summary>
    /// <paramref name="arg"/> as a folder, else <c>&lt;root&gt;\&lt;arg&gt;</c>, else
    /// <c>&lt;root&gt;\done\&lt;arg&gt;</c>; null when none exists.
    /// </summary>
    public static string? Find(string arg, string batchesRoot)
    {
        if (Directory.Exists(arg))
            return Path.GetFullPath(arg);
        foreach (var candidate in new[] { Path.Combine(batchesRoot, arg), Path.Combine(batchesRoot, "done", arg) })
        {
            if (Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
        }
        return null;
    }

    /// <summary>The <c>Takeout\Google Photos</c> folder inside a batch.</summary>
    public static string PhotosRoot(string batchPath) => Path.Combine(batchPath, "Takeout", "Google Photos");
}
