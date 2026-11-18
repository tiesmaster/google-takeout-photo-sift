using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Settings;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;

public enum FrameKind
{
    /// <summary>About a quarter of the screen size: cheap, shown first, and what a held key flies through.</summary>
    Preview,

    /// <summary>Screen size: what you look at.</summary>
    Sharp,
}

/// <summary>A decoded photo (stored pixels), or the error that prevented it.</summary>
public sealed record DecodedFrame(
    string Path,
    FrameKind Kind,
    BitmapSource? Bitmap,
    int StoredWidth,
    int StoredHeight,
    int Orientation,
    (int W, int H) Box,
    long FileBytes,
    double ReadMs,
    double DecodeMs,
    string? Error)
{
    public long SizeBytes => Bitmap is null ? 1024 : (long)Bitmap.PixelWidth * Bitmap.PixelHeight * 4;
}

/// <summary>What the view wants decoded; lower priority numbers go first.</summary>
public readonly record struct FrameRequest(string Path, FrameKind Kind, int Priority);

/// <summary>
/// Decodes photos on background threads and caches the results. The UI thread never decodes or
/// reads files: it asks for the frames it wants with <see cref="Schedule"/> (which replaces the
/// previous wish list, dropping stale requests) and is told about each result through the callback.
/// </summary>
public sealed class FramePipeline : IDisposable
{
    private readonly LruCache<string, DecodedFrame> _sharp;
    private readonly LruCache<string, DecodedFrame> _preview;
    private readonly LruCache<string, byte[]> _bytes;
    private readonly ConcurrentDictionary<string, (int W, int H, int Orientation)> _headers = new(StringComparer.OrdinalIgnoreCase);

    // Rotations made in this session; they win over what's in the file, which ExifTool may not have
    // rewritten yet.
    private readonly ConcurrentDictionary<string, int> _orientationOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<DecodedFrame> _onFrame;
    // A plain object, not System.Threading.Lock: the workers wait on it with Monitor.Wait.
    private readonly object _lock = new();
    private readonly List<FrameRequest> _queue = [];
    private readonly HashSet<(string, FrameKind)> _inFlight = [];
    private readonly List<Thread> _workers = [];
    private volatile bool _stopping;
    private (int W, int H) _box = (1920, 1080);

    public FramePipeline(SiftSettings settings, Action<DecodedFrame> onFrame)
    {
        _onFrame = onFrame;
        _sharp = new(settings.CacheMegabytes * 1024L * 1024, f => f.SizeBytes, StringComparer.OrdinalIgnoreCase);
        _preview = new(96L * 1024 * 1024, f => f.SizeBytes, StringComparer.OrdinalIgnoreCase);
        _bytes = new(settings.FileCacheMegabytes * 1024L * 1024, b => b.Length, StringComparer.OrdinalIgnoreCase);
        WorkerCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        for (var i = 0; i < WorkerCount; i++)
        {
            var thread = new Thread(Work) { IsBackground = true, Name = $"Decode {i + 1}", Priority = ThreadPriority.BelowNormal };
            _workers.Add(thread);
            thread.Start();
        }
    }

    public int WorkerCount { get; }

    /// <summary>The area photos are fitted into, in physical pixels.</summary>
    public (int W, int H) Box
    {
        get
        {
            lock (_lock)
                return _box;
        }
        set
        {
            lock (_lock)
                _box = value;
        }
    }

    /// <summary>A cached sharp frame decoded for (about) the current box, or a cached error.</summary>
    public DecodedFrame? GetSharp(string path) =>
        _sharp.TryGet(path, out var frame) && (frame.Error is not null || FrameGeometry.BoxMatches(frame.Box, Box)) ? frame : null;

    /// <summary>Any cached frame that's worth showing while the sharp one is on its way.</summary>
    public DecodedFrame? GetStandIn(string path)
    {
        if (_sharp.TryGet(path, out var sharp))
            return sharp;
        return _preview.TryGet(path, out var preview) ? preview : null;
    }

    /// <summary>Drops a cached error, e.g. one from while the file was moved away by a delete.</summary>
    public void ForgetErrors(string path)
    {
        if (_sharp.TryGet(path, out var frame) && frame.Error is not null)
            _sharp.Remove(path);
        if (_preview.TryGet(path, out var preview) && preview.Error is not null)
            _preview.Remove(path);
    }

    /// <summary>
    /// Records a rotation. The cached pixels stay valid (only the tag changes), but after a quarter turn
    /// the sharp frame no longer fits the box, so it's dropped and decoded again at the new size.
    /// </summary>
    public void SetOrientation(string path, int orientation)
    {
        _orientationOverrides[path] = orientation;
        if (_headers.TryGetValue(path, out var header))
        {
            _headers[path] = (header.W, header.H, orientation);
            if (FrameGeometry.SwapsAxes(header.Orientation) != FrameGeometry.SwapsAxes(orientation))
                _sharp.Remove(path);
        }
        // ExifTool rewrites the file; don't keep the old bytes around.
        _bytes.Remove(path);
    }

    /// <summary>
    /// Replaces the queue with <paramref name="wanted"/>, minus what's cached or already being decoded.
    /// Requests that are no longer wanted are dropped; decodes already running finish and are cached.
    /// </summary>
    public void Schedule(IEnumerable<FrameRequest> wanted)
    {
        var fresh = wanted
            .Where(r => r.Kind == FrameKind.Sharp ? GetSharp(r.Path) is null : GetStandIn(r.Path) is null)
            .OrderBy(r => r.Priority)
            .ToList();
        lock (_lock)
        {
            _queue.Clear();
            _queue.AddRange(fresh.Where(r => !_inFlight.Contains((r.Path, r.Kind))));
            Monitor.PulseAll(_lock);
        }
    }

    private void Work()
    {
        while (true)
        {
            FrameRequest request;
            lock (_lock)
            {
                while (_queue.Count == 0 && !_stopping)
                    Monitor.Wait(_lock);
                if (_stopping)
                    return;
                request = _queue[0];
                _queue.RemoveAt(0);
                _inFlight.Add((request.Path, request.Kind));
            }
            try
            {
                var frame = Decode(request);
                (frame.Kind == FrameKind.Sharp ? _sharp : _preview).Set(frame.Path, frame);
                if (frame.Error is not null)
                    _sharp.Set(frame.Path, frame);
                _onFrame(frame);
            }
            finally
            {
                lock (_lock)
                    _inFlight.Remove((request.Path, request.Kind));
            }
        }
    }

    private DecodedFrame Decode(FrameRequest request)
    {
        var box = Box;
        var watch = Stopwatch.StartNew();
        byte[] bytes;
        try
        {
            bytes = ReadBytes(request.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failed(request, box, $"can't read: {e.Message}");
        }
        var readMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        try
        {
            var header = _headers.GetOrAdd(request.Path, path =>
            {
                var (w, h) = WicDecoder.ReadSize(bytes);
                return (w, h, _orientationOverrides.TryGetValue(path, out var o) ? o : OrientationReader.Read(bytes));
            });
            var scale = FrameGeometry.FitScale(header.W, header.H, header.Orientation, box.W, box.H);
            var width = Math.Max(1, (int)Math.Round(header.W * scale));
            if (request.Kind == FrameKind.Preview)
                width = Math.Max(1, Math.Min(width, (width + 3) / 4));
            var bitmap = WicDecoder.Decode(bytes, width);
            return new DecodedFrame(request.Path, request.Kind, bitmap, header.W, header.H, header.Orientation, box,
                bytes.Length, readMs, watch.Elapsed.TotalMilliseconds, null);
        }
        catch (Exception e) when (WicDecoder.IsMissingCodec(e)
                                  && Path.GetExtension(request.Path) is var ext
                                  && (ext.Equals(".heic", StringComparison.OrdinalIgnoreCase) || ext.Equals(".heif", StringComparison.OrdinalIgnoreCase)))
        {
            return Failed(request, box, "HEIC can't be shown yet: the Magick.NET decoder arrives in milestone 2");
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or IOException or ArgumentException
                                  or InvalidOperationException or System.Runtime.InteropServices.COMException or OverflowException)
        {
            return Failed(request, box, $"can't open: {e.Message}");
        }
    }

    private static DecodedFrame Failed(FrameRequest request, (int W, int H) box, string error) =>
        new(request.Path, request.Kind, null, 0, 0, 1, box, 0, 0, 0, error);

    private byte[] ReadBytes(string path)
    {
        if (_bytes.TryGet(path, out var cached))
            return cached;
        // FileShare.Delete: ExifTool replaces files while we read, and deletes move them.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1, FileOptions.SequentialScan);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        _bytes.Set(path, bytes);
        return bytes;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _stopping = true;
            Monitor.PulseAll(_lock);
        }
        foreach (var worker in _workers)
            worker.Join(TimeSpan.FromSeconds(2));
    }
}
