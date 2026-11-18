using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Tests.Imaging;

public class DecoderTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    // The fixtures are 32×16 with a red top-left corner (see tools/make_fixtures.py).
    private static Color TopLeft(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new System.Windows.Int32Rect(1, 1, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }

    [Fact]
    public void Wic_returns_the_stored_pixels_ignoring_exif_orientation()
    {
        var bytes = Fixture("orientation-6.jpg");
        Assert.Equal((32, 16), WicDecoder.ReadSize(bytes));

        var bitmap = WicDecoder.Decode(bytes, 32);

        Assert.Equal((32, 16), (bitmap.PixelWidth, bitmap.PixelHeight));
        Assert.True(bitmap.IsFrozen);
        Assert.True(TopLeft(bitmap).R > 150, "the red corner should still be top-left");
    }

    [Fact]
    public void Wic_scales_down_to_the_requested_width()
    {
        var bitmap = WicDecoder.Decode(Fixture("orientation-1.jpg"), 16);
        Assert.Equal((16, 8), (bitmap.PixelWidth, bitmap.PixelHeight));
    }

    [Fact]
    public void Magick_decodes_heic_as_stored_pixels()
    {
        var bytes = Fixture("orientation-6.heic");
        var (width, height) = MagickDecoder.ReadSize(bytes);

        var bitmap = MagickDecoder.Decode(bytes, width);

        Assert.Equal((width, height), (bitmap.PixelWidth, bitmap.PixelHeight));
        Assert.True(bitmap.IsFrozen);
        Assert.True(TopLeft(bitmap).R > 150, "the red corner should still be top-left");
        Assert.Equal(6, OrientationReader.Read(bytes));
    }

    [Fact]
    public void Magick_scales_down_keeping_the_aspect_ratio()
    {
        var bytes = Fixture("orientation-6.heic");
        var (width, height) = MagickDecoder.ReadSize(bytes);

        var bitmap = MagickDecoder.Decode(bytes, width / 2);

        Assert.Equal((width / 2, height / 2), (bitmap.PixelWidth, bitmap.PixelHeight));
    }

    /// <summary>
    /// On real iPhone HEICs (from local/orientations.json, see tools/export_golden.py): photos with EXIF
    /// orientation 6 or 8 are stored landscape and carry an irot box. If MagickDecoder applied irot, they
    /// would come out portrait and the view would rotate them a second time.
    /// </summary>
    [Fact]
    public void Magick_does_not_apply_irot_to_real_iphone_photos()
    {
        var list = FindLocal("orientations.json");
        Assert.SkipWhen(list is null, "No local/orientations.json; run tools/export_golden.py");
        var heics = JsonSerializer.Deserialize<Dictionary<string, int?>>(File.ReadAllText(list!))!
            .Where(kv => kv.Value is 6 or 8 && kv.Key.EndsWith(".heic", StringComparison.OrdinalIgnoreCase) && File.Exists(kv.Key))
            .Take(5)
            .ToList();
        Assert.SkipWhen(heics.Count == 0, "No rotated HEICs found on this machine");
        foreach (var path in heics.Select(kv => kv.Key))
        {
            var bytes = File.ReadAllBytes(path);
            var (width, height) = MagickDecoder.ReadSize(bytes);
            var bitmap = MagickDecoder.Decode(bytes, width / 8);
            Assert.True(width > height, $"{path}: stored {width}×{height} should be landscape");
            Assert.True(bitmap.PixelWidth > bitmap.PixelHeight, $"{path}: decoded {bitmap.PixelWidth}×{bitmap.PixelHeight}");

            // Proof that the define matters: by default libheif applies irot and the photo comes out portrait.
            using var plain = new ImageMagick.MagickImage(bytes);
            Assert.True(plain.Height > plain.Width, $"{path}: without preserve-orientation {plain.Width}×{plain.Height}");
        }
    }

    private static string? FindLocal(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GoogleTakeoutPhotoSift.slnx")))
            {
                var path = Path.Combine(dir.FullName, "local", name);
                return File.Exists(path) ? path : null;
            }
        }
        return null;
    }
}
