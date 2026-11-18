using System.Globalization;
using System.IO;
using System.Text;

namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Diagnostics;

/// <summary>How the first frame of a navigation was found.</summary>
public enum CacheHit
{
    Sharp,
    Preview,
    Miss,
}

/// <summary>One navigation, from the key press to the sharp frame on screen.</summary>
public sealed class NavigationTiming(int photoNumber, string path, long startTimestamp)
{
    public int PhotoNumber { get; } = photoNumber;
    public string Path { get; } = path;
    public long StartTimestamp { get; } = startTimestamp;
    public CacheHit? FirstHit { get; set; }
    public double? FirstMs { get; set; }
    public double? SharpMs { get; set; }
    public double ReadMs { get; set; }
    public double DecodeMs { get; set; }
    public long FileBytes { get; set; }
    public double Megapixels { get; set; }
    public bool Completed { get; set; }
}

/// <summary>
/// Keeps the last 100 navigations for the overlay and appends every one to
/// <c>%LOCALAPPDATA%\GoogleTakeoutPhotoSift\timing.csv</c>.
/// </summary>
public sealed class TimingLog : IDisposable
{
    private const int Keep = 100;
    private readonly Queue<NavigationTiming> _recent = new();
    private readonly StreamWriter? _csv;

    public TimingLog(string csvPath)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(csvPath)!);
            var isNew = !File.Exists(csvPath);
            _csv = new StreamWriter(csvPath, append: true, Encoding.UTF8);
            if (isNew)
                _csv.WriteLine("time,photo,path,megapixels,bytes,firstHit,firstMs,sharpMs,readMs,decodeMs");
        }
        catch (IOException)
        {
            _csv = null; // another instance has it open; timing still shows in the overlay
        }
    }

    public NavigationTiming? Last { get; private set; }

    public void Complete(NavigationTiming timing)
    {
        if (timing.Completed)
            return;
        timing.Completed = true;
        Last = timing;
        _recent.Enqueue(timing);
        while (_recent.Count > Keep)
            _recent.Dequeue();
        _csv?.WriteLine(string.Join(",",
            DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
            timing.PhotoNumber.ToString(CultureInfo.InvariantCulture),
            Csv(timing.Path),
            timing.Megapixels.ToString("0.0", CultureInfo.InvariantCulture),
            timing.FileBytes.ToString(CultureInfo.InvariantCulture),
            timing.FirstHit?.ToString() ?? "",
            Ms(timing.FirstMs), Ms(timing.SharpMs), Ms(timing.ReadMs), Ms(timing.DecodeMs)));
    }

    /// <summary>The overlay text: the last navigation, then p50/p95 for cached and uncached ones.</summary>
    public string Summary(string extra)
    {
        var sb = new StringBuilder();
        if (Last is { } last)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"last  #{last.PhotoNumber}  {last.FirstHit}  first {Ms(last.FirstMs)}  sharp {Ms(last.SharpMs)} ms");
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"      read {Ms(last.ReadMs)}  decode {Ms(last.DecodeMs)} ms  {last.Megapixels:0.0} MP  {last.FileBytes / 1e6:0.0} MB");
        }
        var cached = _recent.Where(t => t.FirstHit == CacheHit.Sharp).Select(t => t.FirstMs).OfType<double>().ToList();
        var uncached = _recent.Where(t => t.FirstHit != CacheHit.Sharp && t.SharpMs is not null).ToList();
        sb.AppendLine(CultureInfo.InvariantCulture, $"cached    n={cached.Count,-3} shown p50 {P(cached, 50)}  p95 {P(cached, 95)} ms   (target < 50)");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"uncached  n={uncached.Count,-3} first p50 {P(uncached.Select(t => t.FirstMs).OfType<double>(), 50)}  p95 {P(uncached.Select(t => t.FirstMs).OfType<double>(), 95)} ms   (target < 50)");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"                sharp p50 {P(uncached.Select(t => t.SharpMs).OfType<double>(), 50)}  p95 {P(uncached.Select(t => t.SharpMs).OfType<double>(), 95)} ms   (target < 200)");
        sb.Append(extra);
        return sb.ToString();
    }

    private static string P(IEnumerable<double> values, int percentile)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
            return "-";
        var i = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(i, 0, sorted.Count - 1)].ToString("0", CultureInfo.InvariantCulture);
    }

    private static string Ms(double? ms) => ms?.ToString("0.0", CultureInfo.InvariantCulture) ?? "";

    private static string Csv(string s) => s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    public void Dispose() => _csv?.Dispose();
}
