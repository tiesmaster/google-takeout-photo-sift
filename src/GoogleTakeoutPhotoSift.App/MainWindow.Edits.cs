using System.Diagnostics;
using System.IO;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Files;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;
using ExifOrientation = Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif.Orientation;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App;

// Rotate, delete and undo. The screen changes at once; the disk changes on the file-op worker, in order,
// and failures are put right when the worker reports them.
public partial class MainWindow
{
    private sealed record DeleteTag(DeleteEntry Entry);

    private sealed record UndoTag(DeleteEntry Entry, bool Reload);

    private bool _closeWhenIdle;
    private bool _forceClose;

    private void Rotate(int degrees)
    {
        if (CurrentPhoto is not { } photo)
            return;
        if (photo.IsVideo)
        {
            SetStatus("videos can't be rotated here");
            return;
        }
        var path = photo.Primary.Path;
        if (!_orientations.TryGetValue(path, out var orientation))
        {
            try
            {
                orientation = OrientationReader.Read(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                SetStatus($"can't read the orientation: {e.Message}", error: true);
                return;
            }
            _orientations[path] = orientation;
        }
        if (ExifOrientation.Rotate(orientation, degrees) is not int rotated)
        {
            SetStatus($"mirrored orientation ({orientation}), not rotated", error: true);
            return;
        }

        _orientations[path] = rotated;
        _pipeline.SetOrientation(path, rotated);
        if (_shownFrame?.Path == path)
            Fit();
        UpdateStrip();
        // After a quarter turn the sharp frame is decoded again at the new size.
        ScheduleAround();
        SetStatus(IsHeic(path)
            ? "saving… (HEIC: whether Immich shows the new rotation is still to be checked, see PLAN.md §2.4)"
            : "saving…");
        _fileOps.Rotate(photo, photo.Files.Select(f => f.Path).ToList(), rotated);
    }

    private void Delete()
    {
        if (CurrentPhoto is not { } photo || _deletions is null)
            return;
        var (entry, work) = _deletions.Delete(photo);
        photo.Deleted = true;
        _fileOps.Enqueue(new WorkOp(new DeleteTag(entry), work));

        var copies = photo.CopyCount > 0 ? $" and {photo.CopyCount} album {(photo.CopyCount == 1 ? "copy" : "copies")}" : "";
        if ((Step(_current, 1) ?? Step(_current, -1)) is int next)
            GoTo(next, 1, repeat: false, Stopwatch.GetTimestamp());
        else
            ShowEmpty();
        SetStatus($"deleted {photo.Primary.Name}{copies} — z to undo");
    }

    private void Undo()
    {
        if (_deletions?.Undo() is not { } undo)
        {
            SetStatus("nothing to undo");
            return;
        }
        var (entry, work) = undo;
        // A delete from an earlier session (or from before a reload) isn't in the index: put the files
        // back, then read the batch again.
        var reload = entry.Photo is null || IndexOf(entry.Photo) < 0;
        _fileOps.Enqueue(new WorkOp(new UndoTag(entry, reload), work));
        if (reload)
        {
            SetStatus($"restoring {entry.PhotoRelative} (deleted in an earlier session)…");
            return;
        }
        entry.Photo!.Deleted = false;
        GoTo(IndexOf(entry.Photo), 1, repeat: false, Stopwatch.GetTimestamp());
        SetStatus($"restored {entry.Photo.Primary.Name}");
    }

    private async void OnFileOpCompleted(FileOp op, string? error)
    {
        switch (op.Tag)
        {
            case Photo photo when op is RotateOp:
                if (error is not null)
                {
                    SetStatus(error, error: true);
                    // Show what the file really says now.
                    var path = photo.Primary.Path;
                    try
                    {
                        var actual = OrientationReader.Read(path);
                        _orientations[path] = actual;
                        _pipeline.SetOrientation(path, actual);
                        if (_shownFrame?.Path == path)
                            Fit();
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                    }
                }
                else if (CurrentPhoto == photo && _fileOps.Pending == 0)
                {
                    SetStatus("saved");
                }
                break;

            case DeleteTag(var entry) when error is not null:
                // The files stayed where they were (PhotoDeleter put back what it had moved).
                _deletions?.Forget(entry);
                if (entry.Photo is { } failed)
                {
                    failed.Deleted = false;
                    _pipeline.ForgetErrors(failed.Primary.Path);
                    GoTo(IndexOf(failed), 1, repeat: false, Stopwatch.GetTimestamp());
                }
                SetStatus(error, error: true);
                break;

            case UndoTag(var entry, var reload):
                if (error is not null)
                {
                    if (entry.Photo is { } photoBack && !reload)
                    {
                        photoBack.Deleted = true;
                        if (_review!.Photos[_current] == photoBack && (Step(_current, 1) ?? Step(_current, -1)) is int away)
                            GoTo(away, 1, repeat: false, Stopwatch.GetTimestamp());
                    }
                    SetStatus(error, error: true);
                }
                else if (reload)
                {
                    await ReloadAndShowAsync(entry.PhotoRelative);
                }
                else if (entry.Photo is { } restored)
                {
                    // Frames that failed while the files were away can be decoded now.
                    _pipeline.ForgetErrors(restored.Primary.Path);
                    if (CurrentPhoto == restored)
                    {
                        ShowBest();
                        ScheduleAround();
                    }
                }
                break;
        }
    }

    private async Task ReloadAndShowAsync(string photoRelative)
    {
        if (!await ReloadIndexAsync())
            return;
        var index = _review!.Photos
            .Select((p, i) => (p, i))
            .FirstOrDefault(x => string.Equals(Relative(x.p.Primary.Path), photoRelative, StringComparison.OrdinalIgnoreCase), (null!, -1)).Item2;
        _shown = null;
        if (index >= 0)
        {
            _pipeline.ForgetErrors(_review!.Photos[index].Primary.Path);
            GoTo(index, 1, repeat: false, Stopwatch.GetTimestamp());
            SetStatus($"restored {Path.GetFileName(photoRelative)} (deleted in an earlier session)");
        }
        else
        {
            SetStatus($"restored {photoRelative}, but it isn't in the batch's photo list", error: true);
        }
    }

    /// <summary>
    /// Closing waits for pending writes: the first attempt starts waiting and closes again once the
    /// queue is empty (or after two minutes).
    /// </summary>
    private bool ReadyToClose()
    {
        if (_forceClose || _fileOps.Pending == 0)
            return true;
        if (!_closeWhenIdle)
        {
            _closeWhenIdle = true;
            SetStatus($"saving {_fileOps.Pending} change(s) before closing…");
            Task.Run(() => _fileOps.WaitForIdle(TimeSpan.FromMinutes(2))).ContinueWith(_ => Dispatcher.BeginInvoke(() =>
            {
                _forceClose = true;
                Close();
            }));
        }
        return false;
    }

    private int IndexOf(Photo photo)
    {
        var photos = _review!.Photos;
        for (var i = 0; i < photos.Count; i++)
        {
            if (ReferenceEquals(photos[i], photo))
                return i;
        }
        return -1;
    }

    private static bool IsHeic(string path) =>
        Path.GetExtension(path) is var ext
        && (ext.Equals(".heic", StringComparison.OrdinalIgnoreCase) || ext.Equals(".heif", StringComparison.OrdinalIgnoreCase));
}
