using JinxyMac.Core;

namespace JinxyMac.Engine;

/// <summary>
/// Sending mouse input, whichever operating system is underneath.
/// </summary>
/// <remarks>
/// The point of this seam is that everything above it can be run and judged on
/// Windows. Only the macOS implementation is unverifiable without a Mac, so the
/// untested surface of the whole application is one file rather than all of it.
///
/// That matters more than it usually would here: this app is being written by
/// someone with no Mac to try it on.
/// </remarks>
public interface IClickEngine
{
    /// <summary>Whether this engine can actually send input right now.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Why it cannot, when it cannot — shown to the user rather than swallowed.
    /// </summary>
    /// <remarks>
    /// macOS discards synthetic input silently when Accessibility permission is
    /// missing, which is indistinguishable from the app being broken. Saying so
    /// plainly is the difference between a five second fix and a bug report.
    /// </remarks>
    string? Unavailable { get; }

    /// <summary>
    /// Presses a button. The caller is responsible for releasing the same one.
    /// </summary>
    /// <remarks>
    /// The button is a parameter rather than engine state so a press and its
    /// release cannot disagree: there is nothing for a selector change to
    /// mutate between them.
    /// </remarks>
    void MouseDown(ClickButton button);

    void MouseUp(ClickButton button);

    /// <summary>Moves the pointer by a delta, for shake.</summary>
    void MoveBy(int dx, int dy);
}
