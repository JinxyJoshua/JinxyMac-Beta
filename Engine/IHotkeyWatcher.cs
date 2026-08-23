namespace JinxyMac.Engine;

/// <summary>
/// Watches for keys being pressed anywhere, not just in this window.
/// </summary>
/// <remarks>
/// A hotkey that only works while the app has focus is useless: the whole point
/// is triggering it from inside a game. Both platforms can do this, by
/// completely different means, so it sits behind a seam like the click engine.
///
/// Keys are identified by their platform key code rather than a shared enum.
/// Translating between two keyboard models would be a source of bugs for no
/// benefit — nothing here needs to know what the key means, only that the same
/// one was pressed again.
/// </remarks>
public interface IHotkeyWatcher : IDisposable
{
    /// <summary>Whether the watcher is actually able to see keys.</summary>
    bool IsAvailable { get; }

    /// <summary>Why not, when not. Shown rather than swallowed.</summary>
    string? Unavailable { get; }

    /// <summary>
    /// The keys to watch. Replaces the previous set outright.
    /// </summary>
    /// <remarks>
    /// A set rather than a single code because there are three actions bound to
    /// three keys, and one watcher polling all of them is one thread instead of
    /// three doing the same syscall a few microseconds apart.
    /// </remarks>
    void Watch(IEnumerable<int> codes);

    /// <summary>
    /// Raised when a watched key goes down, carrying which one. Not raised on
    /// auto-repeat.
    /// </summary>
    event Action<int>? Pressed;

    /// <summary>
    /// Raised when a watched key comes back up. Only hold mode uses it.
    /// </summary>
    event Action<int>? Released;

    /// <summary>
    /// Captures the next key pressed and reports it, instead of firing Pressed.
    /// </summary>
    void CaptureNext(Action<int, string> captured);

    void Start();
    void Stop();
}
