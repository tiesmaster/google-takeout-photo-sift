using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Files;

/// <summary>One delete that can be undone.</summary>
public sealed class DeleteEntry(string id, string photoRelative, Photo? photo)
{
    public string Id { get; } = id;

    /// <summary>The primary file, relative to <c>Takeout\Google Photos</c>.</summary>
    public string PhotoRelative { get; } = photoRelative;

    /// <summary>The photo in the current index, or null for a delete from an earlier session.</summary>
    public Photo? Photo { get; internal set; } = photo;

    /// <summary>The bulk run this delete belongs to (such as a dedupe), or null.</summary>
    public string? Run { get; internal set; }

    /// <summary>Set by the worker once the files have moved; null while pending or after a failure.</summary>
    public IReadOnlyList<MoveRecord>? Records { get; internal set; }

    internal DeleteEntry WithRecords(IReadOnlyList<MoveRecord> records)
    {
        Records = records;
        return this;
    }
}

/// <summary>
/// Deletes and undos for one batch: what to queue on the file-op worker, the undo stack, and the
/// journal that brings the stack back after a restart.
/// </summary>
public sealed class Deletions
{
    private readonly PhotoDeleter _deleter;
    private readonly List<DeleteEntry> _undo = [];
    private BatchIndex _index;

    public Deletions(BatchIndex index, string batchesRoot)
    {
        _index = index;
        _deleter = new PhotoDeleter(index, batchesRoot);
        Journal = new DeleteJournal(Path.Combine(_deleter.DeletedBatch, "sift-journal.jsonl"));
        foreach (var entry in Journal.PendingDeletes())
            _undo.Add(new DeleteEntry(entry.Id, entry.Photo ?? "?", null) { Run = entry.Run }.WithRecords(entry.Records!));
    }

    public DeleteJournal Journal { get; }

    public string DeletedRoot => _deleter.DeletedRoot;

    /// <summary>Deletes that can be undone, the most recent last.</summary>
    public IReadOnlyList<DeleteEntry> UndoStack => _undo;

    /// <summary>
    /// Switches to a freshly loaded index of the same batch. Entries whose photo isn't in it any more
    /// are undone the way earlier-session deletes are.
    /// </summary>
    public void Use(BatchIndex index)
    {
        _index = index;
        _deleter.Use(index);
        var byPath = index.Photos.ToDictionary(p => Relative(p.Primary.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _undo)
            entry.Photo = byPath.GetValueOrDefault(entry.PhotoRelative);
    }

    /// <summary>
    /// Starts deleting <paramref name="photo"/>: pushes an undo entry and returns the work for the
    /// file-op worker. The work moves the files and journals them; it returns an error or null.
    /// </summary>
    public (DeleteEntry Entry, Func<string?> Work) Delete(Photo photo, string? run = null)
    {
        var entry = new DeleteEntry(Guid.NewGuid().ToString("N"), Relative(photo.Primary.Path), photo) { Run = run };
        _undo.Add(entry);
        return (entry, () =>
        {
            try
            {
                var records = _deleter.Delete(photo);
                entry.Records = records;
                Journal.AppendDelete(entry.Id, entry.PhotoRelative, records, entry.Run);
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return $"delete failed: {e.Message}";
            }
        });
    }

    /// <summary>The most recent bulk run whose name starts with <paramref name="prefix"/> that can still be undone.</summary>
    public string? LastRun(string prefix) =>
        _undo.LastOrDefault(e => e.Run?.StartsWith(prefix, StringComparison.Ordinal) == true)?.Run;

    /// <summary>
    /// Takes every delete of <paramref name="run"/> off the undo stack and returns the work that puts
    /// them back, most recent first.
    /// </summary>
    public List<(DeleteEntry Entry, Func<string?> Work)> UndoRun(string run)
    {
        var result = new List<(DeleteEntry, Func<string?>)>();
        for (var i = _undo.Count - 1; i >= 0; i--)
        {
            if (_undo[i].Run != run)
                continue;
            var entry = _undo[i];
            _undo.RemoveAt(i);
            result.Add((entry, RevertWork(entry)));
        }
        return result;
    }

    /// <summary>Drops the entry of a delete that failed.</summary>
    public void Forget(DeleteEntry entry) => _undo.Remove(entry);

    /// <summary>
    /// Pops the most recent delete and returns the work that puts its files back, or null when there's
    /// nothing to undo.
    /// </summary>
    public (DeleteEntry Entry, Func<string?> Work)? Undo()
    {
        if (_undo.Count == 0)
            return null;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return (entry, RevertWork(entry));
    }

    private Func<string?> RevertWork(DeleteEntry entry) =>
        () =>
        {
            // Runs after the delete itself on the same worker, so Records is set unless it failed.
            if (entry.Records is not { } records)
                return null;
            try
            {
                _deleter.Revert(records);
                Journal.AppendUndo(entry.Id);
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return $"undo failed: {e.Message}";
            }
        };

    private string Relative(string path) => Path.GetRelativePath(_index.PhotosRoot, path).Replace('\\', '/');
}
