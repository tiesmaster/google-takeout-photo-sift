using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Exif;

/// <summary>What ExifTool printed for one command.</summary>
public sealed record ExifToolResult(IReadOnlyList<string> Output, IReadOnlyList<string> Errors);

/// <summary>
/// One long-running <c>exiftool -stay_open</c> process, so each command costs milliseconds instead of
/// a process start. Each command ends with a numbered <c>{readyN}</c> marker on stdout and stderr, so
/// answers can't get mixed up. Not thread-safe by itself: one caller at a time (the file-op worker).
/// </summary>
public sealed partial class ExifToolProcess : IDisposable
{
    private readonly Process _process;
    private readonly BlockingCollection<string> _stdout = new();
    private readonly BlockingCollection<string> _stderr = new();
    private readonly Lock _lock = new();
    private int _nextId = 1;
    private bool _disposed;

    public ExifToolProcess(string exifToolPath = "exiftool")
    {
        var info = new ProcessStartInfo(exifToolPath)
        {
            ArgumentList = { "-stay_open", "True", "-@", "-" },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // No BOM: ExifTool would read it as part of the first argument.
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            _process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {exifToolPath}");
        }
        catch (Win32Exception e)
        {
            throw new InvalidOperationException($"Could not start ExifTool ('{exifToolPath}'): {e.Message}. Is it on PATH?", e);
        }
        StartReader(_process.StandardOutput, _stdout);
        StartReader(_process.StandardError, _stderr);
    }

    /// <summary>Runs one command (one argument per item) and waits for its answer.</summary>
    public ExifToolResult Run(params IEnumerable<string> args)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var id = _nextId++;
            var marker = $"{{ready{id}}}";
            var input = _process.StandardInput;
            input.Write("-charset\nfilename=utf8\n");
            foreach (var arg in args)
            {
                if (arg.Contains('\n'))
                    throw new ArgumentException("ExifTool arguments can't contain line breaks", nameof(args));
                input.Write(arg);
                input.Write('\n');
            }
            // -echo4 prints the marker on stderr after the command, -executeN on stdout.
            input.Write($"-echo4\n{marker}\n-execute{id}\n");
            input.Flush();
            var output = ReadUntil(_stdout, marker);
            var errors = ReadUntil(_stderr, marker);
            return new ExifToolResult(output, errors);
        }
    }

    /// <summary>
    /// Sets the Orientation tag losslessly in all <paramref name="paths"/>, keeping their modified date.
    /// Returns null on success, else the error.
    /// </summary>
    public string? WriteOrientation(int orientation, IReadOnlyList<string> paths)
    {
        var result = Run(["-Orientation#=" + orientation.ToString(CultureInfo.InvariantCulture), "-P", "-overwrite_original", .. paths]);
        return CheckWrite(result, paths.Count);
    }

    /// <summary>Reads the Orientation tag (1–8), or null when there isn't one.</summary>
    public int? ReadOrientation(string path)
    {
        var result = Run("-s3", "-n", "-Orientation", path);
        return result.Output.Count > 0 && int.TryParse(result.Output[0], CultureInfo.InvariantCulture, out var o) ? o : null;
    }

    [GeneratedRegex(@"^\s*(\d+) image files? (updated|unchanged)")]
    private static partial Regex CountRegex();

    /// <summary>
    /// Success means every file was updated (or already had the value) and nothing was reported as an
    /// error or as not updated.
    /// </summary>
    internal static string? CheckWrite(ExifToolResult result, int expectedFiles)
    {
        var done = 0;
        foreach (var line in result.Output)
        {
            var m = CountRegex().Match(line);
            if (m.Success)
                done += int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        var problems = result.Output.Concat(result.Errors)
            .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.Contains("weren't updated", StringComparison.Ordinal))
            .ToList();
        if (done == expectedFiles && problems.Count == 0)
            return null;
        var all = result.Output.Concat(result.Errors).Where(l => l.Length > 0);
        return "ExifTool: " + string.Join(" | ", problems.Count > 0 ? problems : all);
    }

    private static List<string> ReadUntil(BlockingCollection<string> lines, string marker)
    {
        var result = new List<string>();
        foreach (var line in lines.GetConsumingEnumerable())
        {
            if (line.Trim() == marker)
                return result;
            result.Add(line.Trim());
        }
        throw new InvalidOperationException("ExifTool stopped unexpectedly");
    }

    private static void StartReader(StreamReader reader, BlockingCollection<string> lines)
    {
        var thread = new Thread(() =>
        {
            try
            {
                while (reader.ReadLine() is { } line)
                    lines.Add(line);
            }
            catch (IOException)
            {
            }
            finally
            {
                lines.CompleteAdding();
            }
        }) { IsBackground = true, Name = "ExifTool reader" };
        thread.Start();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                _process.StandardInput.Write("-stay_open\nFalse\n");
                _process.StandardInput.Flush();
                if (!_process.WaitForExit(10_000))
                    _process.Kill();
            }
            catch (Exception e) when (e is IOException or InvalidOperationException)
            {
            }
            _process.Dispose();
        }
    }
}
