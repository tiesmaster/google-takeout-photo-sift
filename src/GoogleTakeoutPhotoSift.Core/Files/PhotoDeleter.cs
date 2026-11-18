using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Files;

public enum MoveKind
{
    /// <summary>Moved to the deleted tree.</summary>
    Move,

    /// <summary>Copied: a sidecar still used by another file (an <c>-edited</c> copy, a video).</summary>
    Copy,

    /// <summary>The identical file was already in the deleted tree, so the source was removed.</summary>
    Dedup,

    /// <summary>A copy that wasn't needed: the identical file was already there.</summary>
    None,
}

public sealed record MoveRecord(MoveKind Kind, string Source, string Destination);

/// <summary>
/// Moves a photo, its album copies, their sidecars and Live Photo videos to
/// <c>batches\deleted\&lt;batch&gt;\</c>, in the same folder structure, and back. Nothing is ever
/// deleted outright: a source file is only removed when the identical file is already in the deleted
/// tree. A port of <c>delete</c>, <c>move</c> and <c>revert</c> in rotate_viewer.py.
/// </summary>
public sealed class PhotoDeleter
{
    private BatchIndex _index;

    public PhotoDeleter(BatchIndex index, string batchesRoot)
    {
        _index = index;
        var batchName = Path.GetFileName(Path.TrimEndingDirectorySeparator(index.BatchPath));
        DeletedBatch = Path.Combine(batchesRoot, "deleted", batchName);
    }

    /// <summary><c>batches\deleted\&lt;batch&gt;</c>.</summary>
    public string DeletedBatch { get; }

    public string DeletedRoot => Path.Combine(DeletedBatch, "Takeout", "Google Photos");

    /// <summary>Points the deleter at a freshly loaded index of the same batch.</summary>
    public void Use(BatchIndex index) => _index = index;

    /// <summary>
    /// Moves all files of <paramref name="photo"/>. On failure everything done so far is put back and
    /// the exception is rethrown.
    /// </summary>
    public List<MoveRecord> Delete(Photo photo)
    {
        var moves = new List<MoveRecord>();
        try
        {
            foreach (var file in photo.Files)
            {
                MoveMedia(file.Path, moves);
                if (_index.LivePhotoVideos.TryGetValue(file.Path, out var video) && File.Exists(video))
                    MoveMedia(video, moves);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Revert(moves);
            throw;
        }
        return moves;
    }

    /// <summary>Puts the files of a delete back, in reverse order.</summary>
    public void Revert(IReadOnlyList<MoveRecord> moves)
    {
        for (var i = moves.Count - 1; i >= 0; i--)
        {
            var (kind, source, destination) = moves[i];
            switch (kind)
            {
                case MoveKind.Copy:
                    File.Delete(destination);
                    break;
                case MoveKind.Dedup:
                    Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                    File.Copy(destination, source);
                    break;
                case MoveKind.Move:
                    Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                    File.Move(destination, source);
                    break;
            }
            // Only media files have a sidecar entry; they use their sidecar again.
            if (_index.Sidecars.TryGetValue(source, out var json) && json is not null
                && _index.SidecarUsers.TryGetValue(json, out var users))
            {
                users.Add(source);
            }
        }
    }

    private void MoveMedia(string path, List<MoveRecord> moves)
    {
        moves.Add(Move(MoveKind.Move, path));
        if (_index.Sidecars.TryGetValue(path, out var json) && json is not null)
        {
            var users = _index.SidecarUsers[json];
            users.Remove(path);
            // An -edited copy (or video) still using this sidecar keeps the original; copy it instead.
            moves.Add(Move(users.Count > 0 ? MoveKind.Copy : MoveKind.Move, json));
        }
    }

    private MoveRecord Move(MoveKind kind, string source)
    {
        var destination = Path.Combine(DeletedRoot, Path.GetRelativePath(_index.PhotosRoot, source));
        if (File.Exists(destination))
        {
            // A sidecar shared with an -edited copy deleted earlier is already there.
            if (!BatchIndex.FilesAreIdentical(source, destination))
                throw new IOException($"{destination} already exists with different content");
            if (kind == MoveKind.Move)
            {
                File.Delete(source);
                return new MoveRecord(MoveKind.Dedup, source, destination);
            }
            return new MoveRecord(MoveKind.None, source, destination);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (kind == MoveKind.Copy)
            File.Copy(source, destination);
        else
            File.Move(source, destination);
        return new MoveRecord(kind, source, destination);
    }
}
