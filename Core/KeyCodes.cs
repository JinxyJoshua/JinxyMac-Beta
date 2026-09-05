namespace JinxyMac.Core;

/// <summary>
/// Translates a typed letter or digit into the running platform's own key
/// code — a CGKeyCode on macOS, a Windows virtual-key code on Windows.
/// </summary>
/// <remarks>
/// Plain C#, deliberately: <c>Core/</c> has no Avalonia dependency and this
/// must not give it one. The two platforms number the same keys completely
/// differently — see <see cref="Mac"/> for the proof — so anything that used
/// to write <c>(int)someChar</c> and call it a key code was only ever correct
/// on Windows, and only by coincidence.
/// </remarks>
public static class KeyCodes
{
    /// <summary>The running platform's key code for a typed letter or digit, or null.</summary>
    public static int? For(char letterOrDigit)
    {
        if (OperatingSystem.IsMacOS()) return Mac(letterOrDigit);

        char upper = char.ToUpperInvariant(letterOrDigit);

        // Windows' virtual-key codes for letters and digits equal their
        // uppercase ASCII value — VK_A is 0x41, same as 'A'; VK_1 is 0x31,
        // same as '1'. That is a coincidence of the Win32 design, not a rule
        // this class is applying: nothing about a key code has to equal the
        // character printed on the key, and macOS's own codes (see Mac
        // below) prove it doesn't in general. This coincidence is exactly
        // what let a bare `(int)c` pass for a "virtual key code" for as long
        // as it did.
        return char.IsLetterOrDigit(upper) ? upper : null;
    }

    /// <summary>
    /// The macOS table on its own, so it can be proven correct in a test that
    /// runs anywhere — not only on a Mac, which nobody on this project has on
    /// their desk.
    /// </summary>
    /// <remarks>
    /// Copied from, and must be kept in step with, the letter and digit
    /// entries of <c>Engine/MacHotkeyWatcher.cs</c>'s <c>Name(int)</c> switch
    /// — inverted from code-to-name back to name-to-code. Not computed: the
    /// hardware order is neither alphabetical nor numeric (5 and 6 really are
    /// swapped — code 23 is "5", code 22 is "6"), so a formula would be wrong
    /// exactly where getting it right mattered.
    /// </remarks>
    internal static int? Mac(char letterOrDigit) => char.ToUpperInvariant(letterOrDigit) switch
    {
        // Letters.
        'A' => 0, 'B' => 11, 'C' => 8, 'D' => 2, 'E' => 14, 'F' => 3,
        'G' => 5, 'H' => 4, 'I' => 34, 'J' => 38, 'K' => 40, 'L' => 37,
        'M' => 46, 'N' => 45, 'O' => 31, 'P' => 35, 'Q' => 12, 'R' => 15,
        'S' => 1, 'T' => 17, 'U' => 32, 'V' => 9, 'W' => 13, 'X' => 7,
        'Y' => 16, 'Z' => 6,

        // Digits. Not in order in the hardware, and 5 and 6 really are swapped.
        '0' => 29, '1' => 18, '2' => 19, '3' => 20, '4' => 21, '5' => 23,
        '6' => 22, '7' => 26, '8' => 28, '9' => 25,

        _ => null
    };
}
