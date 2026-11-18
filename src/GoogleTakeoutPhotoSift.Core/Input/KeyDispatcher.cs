namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Input;

/// <summary>What a key press did.</summary>
/// <param name="Command">The command to run, if any.</param>
/// <param name="Handled">Whether the key belongs to us (so the UI marks it handled).</param>
/// <param name="Hint">Text to show for a moment: the keys that can follow, or that a sequence didn't exist.</param>
public readonly record struct DispatchResult(Command? Command, bool Handled, string? Hint = null)
{
    public static DispatchResult Unhandled => new(null, false);
}

/// <summary>
/// Turns key presses into commands using a binding table, with two-key sequences like <c>g a</c>.
/// After a sequence's first key, the next key completes it or cancels it (an unknown second key is
/// not run on its own, as on GitHub); Escape cancels; after <see cref="Timeout"/> it lapses.
/// Commands with <see cref="Repeat.Ignore"/> never fire from a held key, and key repeats never start
/// or complete a sequence, so holding a destructive key acts once.
/// </summary>
public sealed class KeyDispatcher
{
    private static readonly KeyChord Escape = new("Escape");

    private readonly Dictionary<KeyChord, Binding> _single = [];
    private readonly Dictionary<(KeyChord First, KeyChord Second), Binding> _sequences = [];
    private readonly TimeProvider _time;
    private KeyChord? _pending;
    private DateTimeOffset _pendingSince;

    public KeyDispatcher(IEnumerable<Binding>? bindings = null, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        foreach (var binding in bindings ?? Bindings.All)
        {
            foreach (var key in binding.Keys)
            {
                var chords = key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(KeyChord.Parse).ToArray();
                var added = chords.Length switch
                {
                    1 => _single.TryAdd(chords[0], binding),
                    2 => _sequences.TryAdd((chords[0], chords[1]), binding),
                    _ => throw new ArgumentException($"'{key}': only one- and two-key sequences are supported"),
                };
                if (!added)
                    throw new ArgumentException($"'{key}' is bound twice (also to {Describe(chords)})");
            }
        }
        foreach (var (first, _) in _sequences.Keys)
        {
            if (_single.TryGetValue(first, out var clash))
                throw new ArgumentException($"'{first}' starts a sequence, so it can't also be bound to {clash.Command} on its own");
        }

        string Describe(KeyChord[] chords) =>
            chords.Length == 1 ? _single[chords[0]].Command.ToString() : _sequences[(chords[0], chords[1])].Command.ToString();
    }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>The first key of a sequence in progress, or null.</summary>
    public KeyChord? Pending => _pending is not null && _time.GetUtcNow() - _pendingSince <= Timeout ? _pending : null;

    public void CancelPending() => _pending = null;

    public DispatchResult Dispatch(KeyChord chord, bool isRepeat)
    {
        if (Pending is { } prefix)
        {
            if (isRepeat)
                return new DispatchResult(null, true);
            _pending = null;
            if (chord == Escape)
                return new DispatchResult(null, true);
            if (_sequences.TryGetValue((prefix, chord), out var completed))
                return new DispatchResult(completed.Command, true);
            return new DispatchResult(null, true, $"{prefix} {chord} — no such shortcut");
        }
        _pending = null;

        if (_sequences.Keys.Any(k => k.First == chord))
        {
            if (isRepeat)
                return new DispatchResult(null, true);
            _pending = chord;
            _pendingSince = _time.GetUtcNow();
            return new DispatchResult(null, true, HintFor(chord));
        }
        if (_single.TryGetValue(chord, out var binding))
            return new DispatchResult(isRepeat && binding.Repeat == Repeat.Ignore ? null : binding.Command, true);
        return DispatchResult.Unhandled;
    }

    /// <summary><c>g …  g first · a albums · l loose</c>: what can follow <paramref name="prefix"/>.</summary>
    public string HintFor(KeyChord prefix) =>
        $"{prefix} …  " + string.Join(" · ", _sequences
            .Where(kv => kv.Key.First == prefix)
            .Select(kv => $"{kv.Key.Second} {kv.Value.Short ?? kv.Value.Description}"));
}
