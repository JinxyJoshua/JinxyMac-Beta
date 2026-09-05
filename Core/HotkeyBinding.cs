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
    /// -1, not 0: on macOS 0 is a real key (<c>kVK_ANSI_A</c>), so a sentinel
    /// meaning "no key at all" has to be a value no key can take. Every
    /// settings and macro file already on disk was written when 0 meant
    /// unbound and A could not be bound — <c>AppSettings.SchemaVersion</c> and
    /// <c>MacroStore</c>'s own file-level version are what tell an old stored
    /// 0 (still "unbound") apart from a current one ("A"), and each is
    /// migrated to -1 the first time it is read under the new schema so a
    /// pre-existing unbound hotkey never turns into a bound A the moment this
    /// build opens someone's old file.
    /// </remarks>
    public static readonly HotkeyBinding Unbound = new(-1, "Not set");

    public bool IsValid => Code >= 0;
}
