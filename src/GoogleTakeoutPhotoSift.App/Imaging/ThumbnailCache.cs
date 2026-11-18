using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;

/// <summary>A small picture for the strip, as stored (the view applies the orientation).</summary>
public sealed record Thumbnail(string Path, BitmapSource Bitmap, int Orientation);

/// <summary>
/// Thumbnails for the strip, on one low-priority thread of their own so they never hold up the main
/// photo. JPEGs use their embedded EXIF thumbnail (read without decoding the photo), else a small WIC
/// decode; HEICs are only made from a frame the main pipeline already decoded, because decoding a HEIC
/// just for its thumbnail takes a second.
/// </summary>
public sealed class ThumbnailCache : IDisposable
{
    private const int LongSide = 240;

    private readonly LruCache<string, Thumbnail> _cache;
    private readonly Func<string, DecodedFrame?> _decodedFrame;
    private readonly Action<Thumbnail> _ready;
    private readonly object _lock = new();
    private readonly List<string> _queue = [];
    private readonly ConcurrentDictionary<string, bool> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Thread _worker;
    private bool _stopping;

    /// <param name="decodedFrame">A frame the pipeline already has for a path, if any.</param>
    /// <param name="ready">Called on the worker thread when a thumbnail is made.</param>
    public ThumbnailCache(Func<string, DecodedFrame?> decodedFrame, Action<Thumbnail> ready)
    {
        _decodedFrame = decodedFrame;
        _ready = ready;
        _cache = new LruCache<string, Thumbnail>(160L * 1024 * 1024, t => (long)t.Bitmap.PixelWidth * t.Bitmap.PixelHeight * 4,
            StringComparer.OrdinalIgnoreCase);
        _worker = new Thread(Work) { IsBackground = true, Name = "Thumbnails", Priority = ThreadPriority.Lowest };
        _worker.Start();
    }

    public Thumbnail? Get(string path) => _cache.TryGet(path, out var thumbnail) ? thumbnail : null;

    /// <summary>Replaces the wish list (most wanted first); what's cached is skipped.</summary>
    public void Request(IEnumerable<string> paths)
    {
        var wanted = paths.Where(p => Get(p) is null && !_failed.ContainsKey(p)).ToList();
        lock (_lock)
        {
            _queue.Clear();
            _queue.AddRange(wanted);
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>A main frame arrived: a HEIC (or anything still missing) can be made from it now.</summary>
    public void FromFrame(DecodedFrame frame)
    {
        if (frame.Bitmap is null || Get(frame.Path) is not null)
            return;
        var thumbnail = new Thumbnail(frame.Path, Shrink(frame.Bitmap), frame.Orientation);
        _cache.Set(frame.Path, thumbnail);
        _ready(thumbnail);
    }

    private void Work()
    {
        while (true)
        {
            string path;
            lock (_lock)
            {
                while (_queue.Count == 0 && !_stopping)
                    Monitor.Wait(_lock);
                if (_stopping)
                    return;
                path = _queue[0];
                _queue.RemoveAt(0);
            }
            if (Get(path) is not null)
                continue;
            if (Make(path) is { } thumbnail)
            {
                _cache.Set(path, thumbnail);
                _ready(thumbnail);
            }
        }
    }

    private Thumbnail? Make(string path)
    {
        if (_decodedFrame(path) is { Bitmap: { } bitmap } frame)
            return new Thumbnail(path, Shrink(bitmap), frame.Orientation);
        var extension = Path.GetExtension(path);
        if (extension.Equals(".heic", StringComparison.OrdinalIgnoreCase) || extension.Equals(".heif", StringComparison.OrdinalIgnoreCase))
            return null; // made from the main frame once it's decoded (FromFrame)
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var orientation = OrientationReader.Read(stream);
            stream.Position = 0;
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);
            var frame0 = decoder.Frames[0];
            if (frame0.Thumbnail is { } embedded)
                return new Thumbnail(path, Freeze(embedded), orientation);
            stream.Position = 0;
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            var width = frame0.PixelWidth >= frame0.PixelHeight
                ? LongSide
                : Math.Max(1, (int)Math.Round(LongSide * (double)frame0.PixelWidth / frame0.PixelHeight));
            return new Thumbnail(path, WicDecoder.Decode(bytes, Math.Min(width, frame0.PixelWidth)), orientation);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException
                                  or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _failed[path] = true;
            return null;
        }
    }

    // Copies the pixels so the thumbnail no longer depends on the stream, and freezes it.
    private static BitmapSource Freeze(BitmapSource source)
    {
        var copy = new WriteableBitmap(new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0));
        copy.Freeze();
        return copy;
    }

    private static BitmapSource Shrink(BitmapSource source)
    {
        var scale = Math.Min(1, LongSide / (double)Math.Max(source.PixelWidth, source.PixelHeight));
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        RenderOptions.SetBitmapScalingMode(scaled, BitmapScalingMode.Fant);
        return Freeze(scaled);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _stopping = true;
            Monitor.PulseAll(_lock);
        }
        _worker.Join(TimeSpan.FromSeconds(2));
    }
}
