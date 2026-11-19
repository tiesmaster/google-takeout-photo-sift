using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;

/// <summary>
/// A 64-bit "difference hash" of a picture as it's shown (EXIF orientation applied): shrink to 9×8
/// grey pixels, and set a bit wherever a pixel is brighter than its right neighbour. The same picture
/// saved twice (even rotated and re-encoded) gives hashes only a few bits apart; different pictures
/// differ in about half the bits.
/// </summary>
public static class Fingerprint
{
    public static ulong Compute(string path, int orientation) => Compute(DecodeSmall(path), orientation);

    public static ulong Compute(BitmapSource stored, int orientation)
    {
        var shown = Shown(stored, orientation);
        var small = new TransformedBitmap(shown, new ScaleTransform(9.0 / shown.PixelWidth, 8.0 / shown.PixelHeight));
        var grey = new FormatConvertedBitmap(small, PixelFormats.Gray8, null, 0);
        var (width, height) = (grey.PixelWidth, grey.PixelHeight);
        var pixels = new byte[width * height];
        grey.CopyPixels(pixels, width, 0);

        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var row = Math.Min(y, height - 1) * width;
                if (pixels[row + Math.Min(x, width - 1)] > pixels[row + Math.Min(x + 1, width - 1)])
                    hash |= 1UL << bit;
                bit++;
            }
        }
        return hash;
    }

    /// <summary>
    /// The picture as shown, squeezed to <paramref name="size"/>×<paramref name="size"/> colour pixels
    /// (BGR bytes), to compare two versions of a photo: see <c>EditedCopies</c>.
    /// </summary>
    public static byte[] Sample(string path, int orientation, int size = 32) => Sample(DecodeSmall(path), orientation, size);

    public static byte[] Sample(BitmapSource stored, int orientation, int size = 32)
    {
        var shown = Shown(stored, orientation);
        var small = new TransformedBitmap(shown, new ScaleTransform((double)size / shown.PixelWidth, (double)size / shown.PixelHeight));
        var colour = new FormatConvertedBitmap(small, PixelFormats.Bgr24, null, 0);
        var pixels = new byte[size * size * 3];
        // Rounding can leave a pixel row or column short; those bytes stay 0 in both samples.
        var (width, height) = (Math.Min(colour.PixelWidth, size), Math.Min(colour.PixelHeight, size));
        colour.CopyPixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, size * 3, 0);
        return pixels;
    }

    private static BitmapSource DecodeSmall(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var extension = Path.GetExtension(path);
        var heif = extension.Equals(".heic", StringComparison.OrdinalIgnoreCase) || extension.Equals(".heif", StringComparison.OrdinalIgnoreCase);
        return heif ? MagickDecoder.Decode(bytes, 64) : WicDecoder.Decode(bytes, 64);
    }

    private static BitmapSource Shown(BitmapSource stored, int orientation)
    {
        var angle = orientation switch
        {
            3 => 180,
            6 => 90,
            8 => 270,
            _ => 0,
        };
        return angle == 0 ? stored : new TransformedBitmap(stored, new RotateTransform(angle));
    }
}
