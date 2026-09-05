using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JinxyMac.Engine;

/// <summary>
/// Keystrokes on Windows, so the app can be run and judged without a Mac.
/// </summary>
/// <remarks>
/// This is not shipped to anyone. It exists because the person writing this
/// application has no Mac, and every hour spent staring at code that cannot be
/// run is an hour of guessing. With this in place the whole app — pages,
/// timing, hotkeys, shake, settings — behaves on Windows exactly as it will on
/// macOS, and the only unverifiable part is MacKeyEngine.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsKeyEngine : IKeyEngine
{
    public bool IsAvailable => OperatingSystem.IsWindows();

    public string? Unavailable => OperatingSystem.IsWindows() ? null : "Not running on Windows.";

    public void KeyDown(int code) => Send(code, up: false);

    public void KeyUp(int code) => Send(code, up: true);

    private static void Send(int virtualKey, bool up)
    {
        if (virtualKey <= 0 || virtualKey > ushort.MaxValue) return;

        uint scan = MapVirtualKey((uint)virtualKey, MapVkToVsc);

        var input = new INPUT[1];

        input[0].type = InputKeyboard;
        input[0].U.ki = new KEYBDINPUT
        {
            wVk = (ushort)virtualKey,
            wScan = (ushort)scan,
            dwFlags = up ? KeyEventUp : 0
        };

        SendInput((uint)input.Length, input, Marshal.SizeOf<INPUT>());
    }

    private const uint InputKeyboard = 1;
    private const uint KeyEventUp = 0x0002;
    private const uint MapVkToVsc = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    /// <remarks>
    /// MOUSEINPUT is declared here despite nothing using it, and it is
    /// load-bearing. A union is as large as its largest member, and SendInput
    /// rejects the call outright if the size it is handed does not match the
    /// real INPUT — silently, by returning zero. Leaving the mouse member out
    /// makes the struct eight bytes short on x64 and nothing is ever sent.
    /// </remarks>
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
