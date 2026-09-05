namespace JinxyMac.Core;

/// <summary>
/// A bindable trigger, stored as this platform's own key code so that keyboard
/// keys and mouse side buttons are the same kind of thing.
/// </summary>
/// <remarks>
/// Named "Code" rather than "VirtualKey" — on macOS these are CGKeyCodes, not
/// Windows virtual-key codes, and the rest of this codebase already calls them
/// that (<c>AppSettings.HotkeyCode</c>, <see cref="Engine.IHotkeyWatcher"/>'s
/// captured <c>code</c>). Captured through <c>IHotkeyWatcher.CaptureNext</c>
/// rather than converted from a WPF <c>Key</c> or <c>MouseButton</c> — there is
/// no WPF here to convert from.
/// </remarks>
public sealed record HotkeyBinding(int Code, string Name)
{
    /// <summary>No key. Polls as never-pressed, and reads as "Not set" on its button.</summary>
    /// <remarks>
    /// Code 0 doubles as two different things: "not set" here, and, on macOS,
    /// the real code of <c>kVK_ANSI_A</c> — so the A key cannot currently be
    /// bound or sent (see the filter at <c>Engine/MacHotkeyWatcher.cs</c>,
    /// which admits only <c>code is &gt; 0 and &lt; 128</c>). Pre-existing, not
    /// introduced here, and not fixed here: every settings file already on disk
    /// uses 0 to mean unbound, so treating 0 as a real, bindable key needs a
    /// migration of that stored meaning, not a change to this sentinel or to
    /// the watcher's filter.
    /// </remarks>
    public static readonly HotkeyBinding Unbound = new(0, "Not set");

    public bool IsValid => Code != 0;
}
