namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Settings;

public enum ReviewOrderMode
{
    /// <summary>Albums first by their first photo's date, then the loose photos (the default).</summary>
    Albums,

    /// <summary>rotate_viewer.py's order and grouping, so photo numbers match the Python viewer.</summary>
    Python,
}

/// <summary>
/// <c>GoogleTakeoutPhotoSift &lt;year | batch path&gt; [--start N] [--order albums|python] [--timing]
/// [--batches-root PATH] [--bench] [--keys "r d z"]</c>. <c>--bench</c> runs a scripted navigation,
/// writes the timing log and window snapshots, and quits. <c>--keys</c> plays the given keys
/// (space-separated, as in the binding table; <c>wait</c> pauses a second), snapshots after each, waits
/// for pending writes and quits.
/// </summary>
public sealed record CommandLine(
    string Batch, int? Start, ReviewOrderMode Order, bool Timing, string? BatchesRoot, bool Bench = false,
    IReadOnlyList<string>? Keys = null)
{
    public const string Usage =
        "Usage: GoogleTakeoutPhotoSift <year | batch path> [--start N] [--order albums|python] [--timing] [--batches-root PATH] [--bench] [--keys \"r d z\"]";

    /// <summary>Parses the arguments; throws <see cref="FormatException"/> with a message for the user.</summary>
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        string? batch = null;
        int? start = null;
        var order = ReviewOrderMode.Albums;
        var timing = false;
        var bench = false;
        IReadOnlyList<string>? keys = null;
        string? root = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--start":
                    if (!int.TryParse(Value(args, ref i, arg), out var n) || n < 1)
                        throw new FormatException("--start needs a photo number (1 or more)");
                    start = n;
                    break;
                case "--order":
                    order = Value(args, ref i, arg).ToLowerInvariant() switch
                    {
                        "albums" => ReviewOrderMode.Albums,
                        "python" => ReviewOrderMode.Python,
                        var other => throw new FormatException($"Unknown order '{other}' (albums or python)"),
                    };
                    break;
                case "--timing":
                    timing = true;
                    break;
                case "--bench":
                    bench = true;
                    break;
                case "--keys":
                    keys = Value(args, ref i, arg).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    break;
                case "--batches-root":
                    root = Value(args, ref i, arg);
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                        throw new FormatException($"Unknown option {arg}");
                    if (batch is not null)
                        throw new FormatException($"Only one batch can be given (got '{batch}' and '{arg}')");
                    batch = arg;
                    break;
            }
        }
        if (batch is null)
            throw new FormatException("Give a year (e.g. 2016) or a batch folder");
        return new CommandLine(batch, start, order, timing, root, bench, keys);
    }

    private static string Value(IReadOnlyList<string> args, ref int i, string option)
    {
        if (i + 1 >= args.Count)
            throw new FormatException($"{option} needs a value");
        return args[++i];
    }
}
