using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;

/// <summary>
/// Still frames of videos: the thumbnail Windows makes for Explorer (through Media Foundation, so .mp4,
/// .mov and .avi). Fetched one at a time on a background thread, in request order, and kept in a small
/// cache; a video without one is remembered as null, so it isn't asked again.
/// </summary>
public sealed class VideoStills : IDisposable
{
    /// <summary>The longest side asked for; Windows may return less.</summary>
    public const int Size = 1280;

    private readonly LruCache<string, BitmapSource?> _cache = new(128L << 20,
        still => still is null ? 0 : (long)still.PixelWidth * still.PixelHeight * 4, StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<string> _queue = new();
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string> _ready;
    private readonly Thread _worker;

    /// <param name="ready">Called on the worker thread when a video's still (or its absence) is known.</param>
    public VideoStills(Action<string> ready)
    {
        _ready = ready;
        // The shell's thumbnail providers are COM objects that expect a single-threaded apartment.
        _worker = new Thread(Work) { IsBackground = true, Name = "video stills" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    /// <summary>True when known: <paramref name="still"/> is the frame, or null if Windows has none.</summary>
    public bool TryGet(string path, out BitmapSource? still) => _cache.TryGet(path, out still);

    public void Request(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!_cache.TryGet(path, out _) && _queued.TryAdd(path, 0))
                _queue.Add(path);
        }
    }

    public void Dispose() => _queue.CompleteAdding();

    private void Work()
    {
        foreach (var path in _queue.GetConsumingEnumerable())
        {
            _cache.Set(path, Read(path));
            _queued.TryRemove(path, out _);
            _ready(path);
        }
    }

    private static BitmapSource? Read(string path)
    {
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            try
            {
                // A real thumbnail only (no file-type icon instead), possibly larger than asked.
                if (factory.GetImage(new NativeSize(Size, Size), ThumbnailOnly | BiggerSizeOk, out var bitmap) != 0)
                    return null;
                try
                {
                    var still = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    still.Freeze();
                    return still;
                }
                finally
                {
                    DeleteObject(bitmap);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception e) when (e is COMException or FileNotFoundException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private const int BiggerSizeOk = 0x1;
    private const int ThumbnailOnly = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeSize(int Width, int Height);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
