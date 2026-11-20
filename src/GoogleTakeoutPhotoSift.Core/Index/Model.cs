namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

/// <summary>One file on disk: a year-folder photo or an album copy.</summary>
/// <param name="Path">Full path.</param>
/// <param name="Folder">Name of its folder under <c>Takeout\Google Photos</c>.</param>
/// <param name="SidecarPath">Its .json sidecar, or null.</param>
/// <param name="TakenUnix">The sidecar's photoTakenTime (unix seconds), or null.</param>
/// <param name="Size">Length in bytes when the batch was indexed.</param>
public sealed record MediaFile(string Path, string Folder, string? SidecarPath, long? TakenUnix, long Size)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// One logical photo: its primary file plus the album copies that are the same photo. Every action
/// (rotate, delete) applies to all of <see cref="Files"/>, so they stay byte-identical.
/// </summary>
public sealed class Photo(IReadOnlyList<MediaFile> files)
{
    /// <summary>Primary first, then the copies in folder order.</summary>
    public IReadOnlyList<MediaFile> Files { get; } = files;

    public MediaFile Primary => Files[0];

    public int CopyCount => Files.Count - 1;

    /// <summary>A video on its own (a Live Photo's video goes along with its photo instead).</summary>
    public bool IsVideo => BatchIndex.IsVideo(Primary.Path);

    /// <summary>
    /// Set while the photo is deleted in this session. The photo keeps its place in the list, so an
    /// undo puts it back exactly where it was; navigation skips it.
    /// </summary>
    public bool Deleted { get; set; }

    /// <summary>Received through WhatsApp, by its file traits: see <see cref="WhatsAppPhotos"/>.</summary>
    public bool IsWhatsApp { get; set; }

    public override string ToString() => $"{Primary.Folder}/{Primary.Name} (+{CopyCount})";
}

/// <summary>An album folder: any folder that isn't "Photos from YYYY".</summary>
public sealed record Album(string Folder, string Title);
