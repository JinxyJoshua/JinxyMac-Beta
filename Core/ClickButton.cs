namespace JinxyMac.Core;

/// <summary>Which physical button the click engine presses.</summary>
public enum ClickButton
{
    Left,
    Right,
    Middle
}

/// <summary>What each button is called where it is shown, and how it is read back.</summary>
/// <remarks>
/// The platform event codes deliberately do not live here. A press and its
/// release are sent separately — the duty cycle puts real time between them —
/// and pairing the wrong two leaves a button held down across the whole
/// desktop with nothing to release it. Each engine owns its own pairing table
/// so that mistake is impossible to make across a platform boundary.
/// </remarks>
public static class ClickButtons
{
    /// <summary>What the button is called where it is shown.</summary>
    public static string Label(ClickButton button) => button switch
    {
        ClickButton.Right => "Right",
        ClickButton.Middle => "Wheel",
        _ => "Left"
    };

    /// <summary>
    /// Reads a stored or tag value back into a button.
    /// </summary>
    /// <remarks>
    /// Anything unrecognised is the left button rather than a failure. A
    /// hand-edited settings file naming a button that does not exist should
    /// leave a working clicker, not one that presses nothing.
    /// </remarks>
    public static ClickButton Parse(string? name) =>
        Enum.TryParse(name, ignoreCase: true, out ClickButton button)
        && Enum.IsDefined(button)
            ? button
            : ClickButton.Left;
}
