using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;

/// <summary>
/// Decodes an image with WIC, scaled down to fit a box, on whatever thread calls it. The result holds
/// the stored pixels: EXIF orientation is applied by the view, so a rotation needs no new decode.
/// </summary>
public static class WicDecoder
{
    /// <summary>The stored width and height, read from the header only.</summary>
    public static (int Width, int Height) ReadSize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    /// <summary>
    /// Decodes <paramref name="bytes"/> at <paramref name="decodeWidth"/> × matching height (stored
    /// orientation), as a frozen Bgr32 bitmap that any thread can use.
    /// </summary>
    public static BitmapSource Decode(byte[] bytes, int decodeWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        // Not IgnoreImageCache: with a StreamSource (no URI) WPF then throws "Key cannot be null".
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.StreamSource = new MemoryStream(bytes, writable: false);
        image.DecodePixelWidth = decodeWidth;
        image.EndInit();

        // Convert here, on the worker, so the render thread doesn't convert 24-bit pixels on first draw.
        BitmapSource result = image.Format == PixelFormats.Bgr32 || image.Format == PixelFormats.Pbgra32
            ? image
            : new WriteableBitmap(new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0));
        result.Freeze();
        return result;
    }
}
