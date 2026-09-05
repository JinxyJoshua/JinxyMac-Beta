using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JinxyMac.Core;

namespace JinxyMac.Engine;

/// <summary>
/// Mouse input on Windows, so the app can be run and judged without a Mac.
/// </summary>
/// <remarks>
/// This is not shipped to anyone. It exists because the person writing this
/// application has no Mac, and every hour spent staring at code that cannot be
/// run is an hour of guessing. With this in place the whole app — pages,
/// timing, hotkeys, shake, settings — behaves on Windows exactly as it will on
/// macOS, and the only unverifiable part is MacClickEngine.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsClickEngine : IClickEngine
{
    public bool IsAvailable => OperatingSystem.IsWindows();

    public string? Unavailable => OperatingSystem.IsWindows() ? null : "Not running on Windows.";

    public void MouseDown(ClickButton button) => Send(DownFlag(button), 0, 0);

    public void MouseUp(ClickButton button) => Send(UpFlag(button), 0, 0);

    private static uint DownFlag(ClickButton button) => button switch
    {
        ClickButton.Right => MouseEventRightDown,
        ClickButton.Middle => MouseEventMiddleDown,
        _ => MouseEventLeftDown
    };

    private static uint UpFlag(ClickButton button) => button switch
    {
        ClickButton.Right => MouseEventRightUp,
        ClickButton.Middle => MouseEventMiddleUp,
        _ => MouseEventLeftUp
    };

    public void MoveBy(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;

        // Relative: MOUSEEVENTF_ABSOLUTE is deliberately absent, so these are
        // offsets rather than screen coordinates.
        Send(MouseEventMove, dx, dy);
    }

    private static void Send(uint flags, int dx, int dy)
    {
        INPUT[] input =
        {
            new INPUT
            {
                type = InputMouse,
                U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } }
            }
        };

        SendInput((uint)input.Length, input, Marshal.SizeOf<INPUT>());
    }

    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
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
}
