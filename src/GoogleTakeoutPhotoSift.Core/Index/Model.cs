namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

/// <summary>One file on disk: a year-folder photo or an album copy.</summary>
/// <param name="Path">Full path.</param>
/// <param name="Folder">Name of its folder under <c>Takeout\Google Photos</c>.</param>
/// <param name="SidecarPath">Its .json sidecar, or null.</param>
/// <param name="TakenUnix">The sidecar's photoTakenTime (unix seconds), or null.</param>
public sealed record MediaFile(string Path, string Folder, string? SidecarPath, long? TakenUnix)
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

    public override string ToString() => $"{Primary.Folder}/{Primary.Name} (+{CopyCount})";
}

/// <summary>An album folder: any folder that isn't "Photos from YYYY".</summary>
public sealed record Album(string Folder, string Title);
