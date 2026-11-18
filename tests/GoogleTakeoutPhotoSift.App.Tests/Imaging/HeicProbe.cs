using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Tests.Imaging;

/// <summary>
/// Why HEIC never goes through WIC: on a machine without the HEVC Video Extensions, WIC "decodes" a
/// real HEIC to a blank 1×1 image instead of failing. Runs on real photos listed in
/// local/orientations.json; skipped elsewhere.
/// </summary>
public class HeicProbe
{
    [Fact]
    public void Magick_gives_real_pixels_for_real_heics()
    {
        var path = RealHeic();
        var bytes = File.ReadAllBytes(path);

        var bitmap = MagickDecoder.Decode(bytes, 400);

        Assert.Equal(400, bitmap.PixelWidth);
        Assert.True(MeanBrightness(bitmap) > 5, $"{path} decoded black");
    }

    private static double MeanBrightness(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        double sum = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            sum += (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3.0;
        return sum / (pixels.Length / 4.0);
    }

    private static string RealHeic()
    {
        string? list = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && list is null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "local", "orientations.json");
            if (File.Exists(Path.Combine(dir.FullName, "GoogleTakeoutPhotoSift.slnx")))
                list = File.Exists(candidate) ? candidate : "";
        }
        Assert.SkipWhen(string.IsNullOrEmpty(list), "No local/orientations.json; run tools/export_golden.py");
        var path = JsonSerializer.Deserialize<Dictionary<string, int?>>(File.ReadAllText(list!))!.Keys
            .FirstOrDefault(k => k.EndsWith(".heic", StringComparison.OrdinalIgnoreCase) && File.Exists(k));
        Assert.SkipWhen(path is null, "No HEIC files on this machine");
        return path!;
    }
}
