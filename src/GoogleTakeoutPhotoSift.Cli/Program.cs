using System.Diagnostics;
using System.IO;
using System.Text;
using Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Batches;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Files;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Index;
using Tiesmaster.GoogleTakeoutPhotoSift.Core.Settings;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Cli;

/// <summary>
/// Batch commands:
///   dedupe &lt;year | batch path&gt; [--apply] [--batches-root PATH]
///   undo-dedupe &lt;year | batch path&gt; [--batches-root PATH]
///   drop-edited &lt;year | batch path&gt; [--apply] [--batches-root PATH]
///   undo-drop-edited &lt;year | batch path&gt; [--batches-root PATH]
/// dedupe finds photos that are in the batch twice (the camera original and an upright re-encoded copy);
/// drop-edited finds the -edited copies that are only a mild enhance of their original. With --apply,
/// both delete like the viewer does: to batches\deleted, journalled, undoable.
/// </summary>
public static class Program
{
    private const string Usage = """
        Usage:
          GoogleTakeoutPhotoSift.Cli dedupe <year | batch path> [--apply] [--batches-root PATH]
          GoogleTakeoutPhotoSift.Cli undo-dedupe <year | batch path> [--batches-root PATH]
          GoogleTakeoutPhotoSift.Cli drop-edited <year | batch path> [--apply] [--batches-root PATH]
          GoogleTakeoutPhotoSift.Cli undo-drop-edited <year | batch path> [--batches-root PATH]

        dedupe and drop-edited without --apply only list what they would do.
        """;

    private static readonly string[] Commands = ["dedupe", "undo-dedupe", "drop-edited", "undo-drop-edited"];

    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        var apply = args.Contains("--apply");
        var rootIndex = Array.IndexOf(args, "--batches-root");
        if (positional.Count < 2 || !Commands.Contains(positional[0]) || (rootIndex >= 0 && rootIndex + 1 >= args.Length))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        var settings = SiftSettings.Load();
        var root = rootIndex >= 0 ? args[rootIndex + 1] : settings.BatchesRoot;
        if (rootIndex >= 0)
            positional.Remove(args[rootIndex + 1]);
        if (BatchLocator.Find(positional[1], root) is not { } batch)
        {
            Console.Error.WriteLine($"No batch folder found for '{positional[1]}' (looked in {root} and {root}\\done).");
            return 2;
        }

        var changes = positional[0].StartsWith("undo-", StringComparison.Ordinal) || apply;
        if (changes && Process.GetProcessesByName("GoogleTakeoutPhotoSift").Length > 0)
        {
            Console.Error.WriteLine("Photo Sift is running. Close it first: it would keep showing the old photo list.");
            return 3;
        }

        var index = BatchIndex.Load(batch);
        return positional[0] switch
        {
            "dedupe" => Dedupe(index, root, apply),
            "drop-edited" => DropEdited(index, root, apply),
            "undo-drop-edited" => UndoRun(index, root, "drop-edited-"),
            _ => UndoRun(index, root, "dedupe-"),
        };
    }

    private static int Dedupe(BatchIndex index, string root, bool apply)
    {
        Console.WriteLine($"Batch {index.BatchPath}: {index.Photos.Count} photos");
        Console.WriteLine("Looking for photos taken at the same second …");
        var watch = Stopwatch.StartNew();
        var report = Twins.Find(index, ReadHeader, (path, orientation) =>
        {
            try
            {
                return Fingerprint.Compute(path, orientation);
            }
            catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or InvalidOperationException
                                      or System.Runtime.InteropServices.COMException or ImageMagick.MagickException)
            {
                return null;
            }
        });
        Console.WriteLine($"Checked in {watch.Elapsed.TotalSeconds:0.0} s.");
        Console.WriteLine();

        var copies = report.Pairs.Sum(p => p.Remove.CopyCount);
        Console.WriteLine($"{report.Pairs.Count} twins. Keep the camera original (orientation 3/6/8), remove the upright re-encoded copy:");
        Console.WriteLine();
        foreach (var pair in report.Pairs)
        {
            var albums = pair.Remove.Files.Skip(1).Select(f => f.Folder).ToList();
            Console.WriteLine($"  keep   {pair.Keep.Primary.Name,-28} {pair.KeepHeader.Width}×{pair.KeepHeader.Height} o{pair.KeepHeader.Orientation}");
            Console.WriteLine($"  remove {pair.Remove.Primary.Name,-28} {pair.RemoveHeader.Width}×{pair.RemoveHeader.Height} o{pair.RemoveHeader.Orientation}"
                              + $"   fingerprint {pair.Distance}/64 apart"
                              + (albums.Count > 0 ? $"   + copy in {string.Join(", ", albums)}" : ""));
        }
        Console.WriteLine();
        if (report.Skipped.Count > 0)
        {
            Console.WriteLine($"{report.Skipped.Count} groups taken at the same second are left alone:");
            foreach (var skip in report.Skipped)
                Console.WriteLine($"  {string.Join(" + ", skip.Photos.Select(p => p.Primary.Name))}: {skip.Reason}");
            Console.WriteLine();
        }

        var deletions = new Deletions(index, root);
        if (!apply)
        {
            Console.WriteLine($"Dry run. With --apply: {report.Pairs.Count} photos and {copies} album copies (with their sidecars) move to");
            Console.WriteLine($"  {deletions.DeletedRoot}");
            Console.WriteLine("and 'undo-dedupe' puts them all back. Nothing was changed.");
            return 0;
        }

        var run = $"dedupe-{DateTime.Now:yyyyMMdd-HHmmss}";
        var failed = 0;
        foreach (var pair in report.Pairs)
        {
            var (_, work) = deletions.Delete(pair.Remove, run);
            if (work() is { } error)
            {
                failed++;
                Console.WriteLine($"  {pair.Remove.Primary.Name}: {error}");
            }
        }
        Console.WriteLine($"Removed {report.Pairs.Count - failed} twins ({run}); {failed} failed. 'undo-dedupe' puts them back.");
        return failed == 0 ? 0 : 1;
    }

    private static int DropEdited(BatchIndex index, string root, bool apply)
    {
        Console.WriteLine($"Batch {index.BatchPath}: {index.Photos.Count} photos");
        Console.WriteLine("Comparing each -edited copy with its original …");
        var watch = Stopwatch.StartNew();
        var report = EditedCopies.Find(index, ReadHeader, (path, orientation) =>
        {
            try
            {
                return Fingerprint.Sample(path, orientation);
            }
            catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or InvalidOperationException
                                      or System.Runtime.InteropServices.COMException or ImageMagick.MagickException)
            {
                return null;
            }
        });
        Console.WriteLine($"Checked in {watch.Elapsed.TotalSeconds:0.0} s.");
        Console.WriteLine();

        var copies = report.Pairs.Sum(p => p.Edited.CopyCount);
        Console.WriteLine($"{report.Pairs.Count} mild edits. Keep the original, remove the -edited copy:");
        Console.WriteLine();
        foreach (var pair in report.Pairs)
        {
            var albums = pair.Edited.Files.Skip(1).Select(f => f.Folder).ToList();
            Console.WriteLine($"  keep {pair.Original.Primary.Name,-28} remove {pair.Edited.Primary.Name,-34} differs {pair.Difference,4:0.0}/255"
                              + (albums.Count > 0 ? $"   + copy in {string.Join(", ", albums)}" : ""));
        }
        Console.WriteLine();
        if (report.Skipped.Count > 0)
        {
            Console.WriteLine($"{report.Skipped.Count} -edited copies are left alone:");
            foreach (var skip in report.Skipped)
                Console.WriteLine($"  {skip.Edited.Primary.Name}: {skip.Reason}");
            Console.WriteLine();
        }

        var deletions = new Deletions(index, root);
        if (!apply)
        {
            Console.WriteLine($"Dry run. With --apply: {report.Pairs.Count} photos and {copies} album copies move to");
            Console.WriteLine($"  {deletions.DeletedRoot}");
            Console.WriteLine("and 'undo-drop-edited' puts them all back. A sidecar the original shares stays with it. Nothing was changed.");
            return 0;
        }

        var run = $"drop-edited-{DateTime.Now:yyyyMMdd-HHmmss}";
        var failed = 0;
        foreach (var pair in report.Pairs)
        {
            var (_, work) = deletions.Delete(pair.Edited, run);
            if (work() is { } error)
            {
                failed++;
                Console.WriteLine($"  {pair.Edited.Primary.Name}: {error}");
            }
        }
        Console.WriteLine($"Removed {report.Pairs.Count - failed} -edited copies ({run}); {failed} failed. 'undo-drop-edited' puts them back.");
        return failed == 0 ? 0 : 1;
    }

    private static int UndoRun(BatchIndex index, string root, string prefix)
    {
        var deletions = new Deletions(index, root);
        if (deletions.LastRun(prefix) is not { } run)
        {
            Console.WriteLine($"No {prefix.TrimEnd('-')} to undo for this batch.");
            return 0;
        }
        var failed = 0;
        var undone = deletions.UndoRun(run);
        foreach (var (entry, work) in undone)
        {
            if (work() is { } error)
            {
                failed++;
                Console.WriteLine($"  {entry.PhotoRelative}: {error}");
            }
        }
        Console.WriteLine($"Put back {undone.Count - failed} photos from {run}; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static ImageHeader? ReadHeader(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var extension = Path.GetExtension(path);
            var (width, height) = extension.Equals(".heic", StringComparison.OrdinalIgnoreCase) || extension.Equals(".heif", StringComparison.OrdinalIgnoreCase)
                ? MagickDecoder.ReadSize(bytes)
                : WicDecoder.ReadSize(bytes);
            return new ImageHeader(width, height, OrientationReader.Read(bytes));
        }
        catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or InvalidOperationException
                                  or System.Runtime.InteropServices.COMException or ImageMagick.MagickException)
        {
            return null;
        }
    }
}
