namespace Tiesmaster.GoogleTakeoutPhotoSift.Core.Input;

/// <summary>
/// Turns key presses into commands using a binding table. Commands with <see cref="Repeat.Ignore"/>
/// never fire from a held key, so holding a destructive key acts once.
/// </summary>
public sealed class KeyDispatcher
{
    private readonly Dictionary<KeyChord, Binding> _byChord = [];

    public KeyDispatcher(IEnumerable<Binding>? bindings = null)
    {
        foreach (var binding in bindings ?? Bindings.All)
        {
            foreach (var key in binding.Keys)
            {
                var chord = KeyChord.Parse(key);
                if (!_byChord.TryAdd(chord, binding))
                    throw new ArgumentException($"'{key}' is bound to both {_byChord[chord].Command} and {binding.Command}");
            }
        }
    }

    /// <summary>The command for <paramref name="chord"/>, or null when it's unbound or a suppressed repeat.</summary>
    public Command? Dispatch(KeyChord chord, bool isRepeat)
    {
        if (!_byChord.TryGetValue(chord, out var binding))
            return null;
        if (isRepeat && binding.Repeat == Repeat.Ignore)
            return null;
        return binding.Command;
    }

    /// <summary>Whether <paramref name="chord"/> is bound at all (so the UI can mark the key as handled).</summary>
    public bool IsBound(KeyChord chord) => _byChord.ContainsKey(chord);
}
