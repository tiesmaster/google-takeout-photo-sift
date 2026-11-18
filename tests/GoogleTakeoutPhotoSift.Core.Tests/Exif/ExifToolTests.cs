using System.Diagnostics;
using System.Security.Cryptography;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Files;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Tests.Exif;

/// <summary>Real ExifTool writes. Skipped when exiftool isn't on PATH.</summary>
public sealed class ExifToolTests : IDisposable
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("exiftool", "-ver") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            p!.WaitForExit(10_000);
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    });

    private readonly string _dir = Directory.CreateTempSubdirectory("photo-sift-exiftool").FullName;

    private static void RequireExifTool() => Assert.SkipUnless(Available.Value, "exiftool isn't on PATH");

    private string Copy(string fixture, string relative)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture), path);
        File.SetLastWriteTimeUtc(path, new DateTime(2016, 5, 1, 12, 0, 0, DateTimeKind.Utc));
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // The image data without metadata, to prove a rotation didn't touch the pixels.
    private string PixelHash(ExifToolProcess exifTool, string path)
    {
        var stripped = Path.Combine(_dir, "stripped-" + Guid.NewGuid() + Path.GetExtension(path));
        File.Copy(path, stripped);
        exifTool.Run("-all=", "-overwrite_original", stripped);
        return Hash(stripped);
    }

    [Fact]
    public void Rotating_a_photo_and_its_copies_keeps_them_identical_and_lossless()
    {
        RequireExifTool();
        string[] paths =
        [
            Copy("orientation-1.jpg", "Photos from 2016/IMG 1.jpg"),
            Copy("orientation-1.jpg", "Café & co [2016]/IMG 1.jpg"),
            Copy("orientation-1.jpg", "Feestje (2)/IMG 1.jpg"),
        ];
        using var exifTool = new ExifToolProcess();
        var pixelsBefore = PixelHash(exifTool, paths[0]);
        var modified = File.GetLastWriteTimeUtc(paths[0]);

        Assert.Null(exifTool.WriteOrientation(6, paths));

        Assert.All(paths, p => Assert.Equal(6, exifTool.ReadOrientation(p)));
        Assert.Single(paths.Select(Hash).Distinct());
        Assert.All(paths, p => Assert.Equal(modified, File.GetLastWriteTimeUtc(p)));
        Assert.Equal(pixelsBefore, PixelHash(exifTool, paths[0]));
        Assert.Equal(6, OrientationReader.Read(paths[0]));
    }

    [Fact]
    public void Rotating_a_heic_changes_its_exif_orientation()
    {
        RequireExifTool();
        var path = Copy("orientation-6.heic", "IMG_9.HEIC");
        using var exifTool = new ExifToolProcess();

        Assert.Null(exifTool.WriteOrientation(3, [path]));

        Assert.Equal(3, OrientationReader.Read(path));
    }

    [Fact]
    public void Writing_the_value_a_file_already_has_counts_as_success()
    {
        RequireExifTool();
        var path = Copy("orientation-6.jpg", "a.jpg");
        using var exifTool = new ExifToolProcess();

        Assert.Null(exifTool.WriteOrientation(6, [path]));
    }

    [Fact]
    public void A_failed_write_is_reported()
    {
        RequireExifTool();
        var path = Copy("orientation-1.jpg", "readonly.jpg");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        using var exifTool = new ExifToolProcess();
        try
        {
            Assert.NotNull(exifTool.WriteOrientation(6, [path, Path.Combine(_dir, "missing.jpg")]));
            Assert.Equal(1, exifTool.ReadOrientation(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Queued_rotations_of_one_photo_become_one_write_and_run_in_order()
    {
        RequireExifTool();
        var path = Copy("orientation-1.jpg", "IMG_1.jpg");
        var completed = new List<(object? Tag, string? Error, int? Orientation)>();
        using var gate = new ManualResetEventSlim();
        using (var queue = new FileOpQueue(() => new ExifToolProcess(), (op, error) =>
               {
                   lock (completed)
                       completed.Add((op.Tag, error, (op as RotateOp)?.Orientation));
               }))
        {
            queue.Enqueue(new WorkOp("gate", () =>
            {
                gate.Wait();
                return null;
            }));
            queue.Rotate("IMG_1", [path], 6);
            queue.Rotate("IMG_1", [path], 3);
            queue.Rotate("IMG_1", [path], 8);
            queue.Enqueue(new WorkOp("after", () => OrientationReader.Read(path) == 8 ? null : "not rotated yet"));
            gate.Set();
            Assert.True(queue.WaitForIdle(TimeSpan.FromSeconds(30)));
        }

        Assert.Equal([("gate", null, null), ("IMG_1", null, 8), ("after", null, null)], completed);
        Assert.Equal(8, OrientationReader.Read(path));
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }
}
