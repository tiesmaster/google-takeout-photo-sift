using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Files;

/// <summary>A change to the disk, run on the file-op worker.</summary>
public abstract class FileOp(object? tag)
{
    /// <summary>What the caller uses to recognise the op when it completes.</summary>
    public object? Tag { get; } = tag;
}

/// <summary>Writes an EXIF orientation to a photo and all its copies.</summary>
public sealed class RotateOp(object tag, IReadOnlyList<string> paths, int orientation) : FileOp(tag)
{
    public IReadOnlyList<string> Paths { get; } = paths;

    /// <summary>Replaced when another rotation of the same photo is queued before this one starts.</summary>
    public int Orientation { get; internal set; } = orientation;
}

/// <summary>Any other change (a delete, an undo): returns an error message, or null on success.</summary>
public sealed class WorkOp(object? tag, Func<string?> work) : FileOp(tag)
{
    public Func<string?> Work { get; } = work;
}

/// <summary>
/// One ordered queue and one worker thread for every change to the disk: rotations, deletes and
/// undos. The order makes a delete queued after a rotation of the same photo move the rotated files,
/// and the UI never waits for the disk. ExifTool starts on the first rotation.
/// </summary>
public sealed class FileOpQueue : IDisposable
{
    private readonly Lazy<ExifToolProcess> _exifTool;
    private readonly Action<FileOp, string?> _completed;
    private readonly LinkedList<FileOp> _queue = new();
    private readonly object _lock = new();
    private readonly Thread _worker;
    private int _running;
    private bool _stopping;

    /// <param name="startExifTool">Starts ExifTool when the first rotation runs.</param>
    /// <param name="completed">Called on the worker thread after each op, with its error or null.</param>
    public FileOpQueue(Func<ExifToolProcess> startExifTool, Action<FileOp, string?> completed)
    {
        _exifTool = new Lazy<ExifToolProcess>(startExifTool, LazyThreadSafetyMode.ExecutionAndPublication);
        _completed = completed;
        _worker = new Thread(Work) { IsBackground = true, Name = "File ops" };
        _worker.Start();
    }

    /// <summary>Ops queued or running.</summary>
    public int Pending
    {
        get
        {
            lock (_lock)
                return _queue.Count + _running;
        }
    }

    /// <summary>
    /// Queues a rotation. When a rotation of the same photo (<paramref name="tag"/>) is still waiting,
    /// it gets the new value instead, so <c>r r r</c> becomes one write.
    /// </summary>
    public void Rotate(object tag, IReadOnlyList<string> paths, int orientation)
    {
        lock (_lock)
        {
            // Only the last op can be replaced: anything queued after a waiting rotation (a delete)
            // must still see the files as that rotation left them.
            if (_queue.Last?.Value is RotateOp waiting && Equals(waiting.Tag, tag))
            {
                waiting.Orientation = orientation;
                return;
            }
            _queue.AddLast(new RotateOp(tag, paths, orientation));
            Monitor.PulseAll(_lock);
        }
    }

    public void Enqueue(WorkOp op)
    {
        lock (_lock)
        {
            _queue.AddLast(op);
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>Waits until every queued op has run. False on timeout.</summary>
    public bool WaitForIdle(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        lock (_lock)
        {
            while (_queue.Count + _running > 0)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !Monitor.Wait(_lock, left))
                    return _queue.Count + _running == 0;
            }
            return true;
        }
    }

    private void Work()
    {
        while (true)
        {
            FileOp op;
            lock (_lock)
            {
                while (_queue.Count == 0 && !_stopping)
                    Monitor.Wait(_lock);
                if (_queue.Count == 0)
                    return;
                op = _queue.First!.Value;
                _queue.RemoveFirst();
                _running = 1;
            }
            string? error;
            try
            {
                error = op switch
                {
                    RotateOp rotate => _exifTool.Value.WriteOrientation(rotate.Orientation, rotate.Paths),
                    WorkOp work => work.Work(),
                    _ => $"unknown op {op.GetType().Name}",
                };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                error = e.Message;
            }
            // Not running any more before reporting, so the callback sees Pending without this op.
            lock (_lock)
                _running = 0;
            try
            {
                _completed(op, error);
            }
            finally
            {
                lock (_lock)
                    Monitor.PulseAll(_lock);
            }
        }
    }

    /// <summary>Runs what's queued, then stops the worker and ExifTool.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _stopping = true;
            Monitor.PulseAll(_lock);
        }
        _worker.Join();
        if (_exifTool.IsValueCreated)
            _exifTool.Value.Dispose();
    }
}
