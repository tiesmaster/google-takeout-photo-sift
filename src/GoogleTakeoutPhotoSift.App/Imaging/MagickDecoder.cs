using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;

/// <summary>
/// Decodes HEIC with Magick.NET (libheif + libde265) when WIC can't, because the HEVC Video
/// Extensions aren't installed (decision 16). Like <see cref="WicDecoder"/> it returns the stored
/// pixels: the HEIF container's own rotation (<c>irot</c>/<c>imir</c>) is not applied, so the view
/// applies the EXIF orientation exactly once, the same way as for JPEG.
/// </summary>
public static class MagickDecoder
{
    private static MagickReadSettings Settings()
    {
        var settings = new MagickReadSettings();
        // Keep the stored pixels and the EXIF orientation as they are, instead of applying irot.
        settings.SetDefine(MagickFormat.Heic, "preserve-orientation", "true");
        return settings;
    }

    /// <summary>The stored width and height, without decoding the pixels.</summary>
    public static (int Width, int Height) ReadSize(byte[] bytes)
    {
        using var image = new MagickImage();
        image.Ping(bytes, Settings());
        return ((int)image.Width, (int)image.Height);
    }

    /// <summary>Decodes and scales to <paramref name="decodeWidth"/> wide, as a frozen Bgra32 bitmap.</summary>
    public static BitmapSource Decode(byte[] bytes, int decodeWidth)
    {
        using var image = new MagickImage(bytes, Settings());
        if (decodeWidth < image.Width)
        {
            var height = Math.Max(1, (uint)Math.Round(image.Height * (double)decodeWidth / image.Width));
            // Scale (box average) instead of Resize: about 300 ms instead of 800 ms for a 24 MP photo, and
            // just as good for reductions this large. The decode itself (~750 ms) can't be made smaller.
            image.Scale(new MagickGeometry((uint)decodeWidth, height) { IgnoreAspectRatio = true });
        }
        var width = (int)image.Width;
        var rows = (int)image.Height;
        var pixels = image.ToByteArray(MagickFormat.Bgra);
        var bitmap = BitmapSource.Create(width, rows, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
