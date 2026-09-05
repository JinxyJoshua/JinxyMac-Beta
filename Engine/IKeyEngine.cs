namespace JinxyMac.Engine;

/// <summary>
/// Sending keystrokes, whichever operating system is underneath.
/// </summary>
/// <remarks>
/// Separate from <see cref="IClickEngine"/> rather than bolted onto it: a
/// build could plausibly send mouse input and not keys, the permissions
/// behind them are checked the same way but reported separately, and the
/// click engine's surface is already the one part of this app that cannot be
/// tested from Windows. Keeping them apart keeps that surface small.
///
/// Down and up rather than a single Tap. The gap between them is the caller's
/// business — the switcher holds a hotbar slot for hundreds of milliseconds,
/// and an engine that owned the timing could not express that.
/// </remarks>
public interface IKeyEngine
{
    /// <summary>Whether this engine can actually send keys right now.</summary>
    bool IsAvailable { get; }

    /// <summary>Why it cannot, when it cannot — shown rather than swallowed.</summary>
    string? Unavailable { get; }

    /// <summary>
    /// Presses a key. The caller is responsible for releasing the same one.
    /// </summary>
    /// <param name="code">
    /// The platform's own key code — a CGKeyCode on macOS, a virtual-key code
    /// on Windows. Deliberately not translated: the hotkey watcher already
    /// speaks the platform's codes, and a translation layer would be a second
    /// table to keep in step with the first.
    /// </param>
    void KeyDown(int code);

    void KeyUp(int code);
}
