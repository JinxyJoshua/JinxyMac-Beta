using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JinxyMac.Engine;

/// <summary>
/// Global hotkeys on macOS, by polling the keyboard rather than tapping it.
/// </summary>
/// <remarks>
/// Polling, not a CGEventTap, and the choice is deliberate. An event tap is the
/// tidier mechanism, but it must be created on a thread with a CFRunLoop, needs
/// re-enabling whenever macOS decides it is too slow, and — the part that
/// matters — sees every keystroke on the machine. That is a lot of machinery
/// and a lot of exposure for one question: is this key down right now.
///
/// CGEventSourceKeyState answers exactly that, reads no content, and costs a
/// syscall. At 8ms the delay is imperceptible and the loop is trivial to reason
/// about, which counts for more than elegance in code that cannot be tested
/// where it is written.
///
/// It still needs Accessibility permission, the same as clicking.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacHotkeyWatcher : IHotkeyWatcher
{
    private CancellationTokenSource? _cts;
    private Action<int, string>? _capture;

    public bool IsAvailable => OperatingSystem.IsMacOS() && AXIsProcessTrusted();

    public string? Unavailable =>
        !OperatingSystem.IsMacOS() ? "Not running on macOS."
        : AXIsProcessTrusted() ? null
        : "Accessibility permission is not granted, so hotkeys cannot be seen.";

    /// <summary>
    /// Replaced wholesale rather than mutated. The poll thread reads this
    /// without a lock, and swapping one reference is atomic where adding to a
    /// list it is walking is not.
    /// </summary>
    private volatile int[] _watched = Array.Empty<int>();

    public event Action<int>? Pressed;

    public event Action<int>? Released;

    /// <summary>
    /// Mouse buttons live above this, as MouseBase plus the button number.
    /// </summary>
    /// <remarks>
    /// Keyboard and mouse are two different questions on macOS —
    /// CGEventSourceKeyState answers one and CGEventSourceButtonState the other
    /// — but a binding is one number either way. Key codes stop at 127, so
    /// putting the buttons at a thousand keeps them in the same int without a
    /// second field to keep in step.
    /// </remarks>
    public const int MouseBase = 1000;

    public void Watch(IEnumerable<int> codes) =>
        _watched = codes.Where(Bindable).Distinct().ToArray();

    private static bool Bindable(int code) =>
        code is > 0 and < 128 || IsMouse(code);

    private static bool IsMouse(int code) => code >= MouseBase;

    /// <summary>Whether a key or button is down right now, whichever it is.</summary>
    private static bool Held(int code) =>
        IsMouse(code)
            ? CGEventSourceButtonState(HidSystemState, (uint)(code - MouseBase))
            : CGEventSourceKeyState(HidSystemState, (ushort)code);

    public void CaptureNext(Action<int, string> captured) => _capture = captured;

    public void Start()
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        new Thread(() => Poll(token))
        {
            IsBackground = true,
            Name = "MacHotkeys"
        }.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private void Poll(CancellationToken token)
    {
        // Sized for the mouse codes as well as the keys, so one array indexes
        // both without a second lookup.
        var down = new bool[MouseBase + 8];

        // The side buttons only. Left is the button this app is busy
        // synthesising and binding it would be a loop; right and middle are
        // wanted in the game.
        int[] buttons = { MouseBase + 3, MouseBase + 4 };

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_capture is { } capture)
                {
                    // Rebinding. Any key or side button ends it, and it must not
                    // also fire the action it was in the middle of binding.
                    var candidates = new List<int>(128 + buttons.Length);
                    for (int code = 0; code < 128; code++) candidates.Add(code);
                    candidates.AddRange(buttons);

                    foreach (int code in candidates)
                    {
                        bool held = Held(code);

                        if (held && !down[code])
                        {
                            _capture = null;
                            capture(code, Name(code));

                            // The chosen key is still physically held. Leaving
                            // it marked down means the press that picked it does
                            // not also count as the first use of the binding —
                            // otherwise choosing a key starts the clicker before
                            // the user has let go of it.
                            Array.Clear(down);
                            down[code] = true;
                            break;
                        }

                        down[code] = held;
                    }
                }
                else
                {
                    foreach (int code in _watched)
                    {
                        bool held = Held(code);

                        // Edge only, both ways: holding must not retrigger for
                        // ever, and hold mode needs the moment it comes back up.
                        if (held && !down[code]) Pressed?.Invoke(code);
                        else if (!held && down[code]) Released?.Invoke(code);

                        down[code] = held;
                    }
                }
            }
            catch
            {
                // Never let a polling failure end the loop.
            }

            Thread.Sleep(PollMs);
        }
    }

    /// <summary>
    /// A readable name for a key code, for the rebind button.
    /// </summary>
    /// <remarks>
    /// Only the keys someone would plausibly bind. Anything else shows its code,
    /// which is ugly but honest and still identifies the key uniquely.
    /// </remarks>
    private static string Name(int code) => code switch
    {
        MouseBase + 3 => "Mouse 4",
        MouseBase + 4 => "Mouse 5",

        0 => "A", 1 => "S", 2 => "D", 3 => "F", 4 => "H", 5 => "G",
        6 => "Z", 7 => "X", 8 => "C", 9 => "V", 11 => "B", 12 => "Q",
        13 => "W", 14 => "E", 15 => "R", 16 => "Y", 17 => "T",
        18 => "1", 19 => "2", 20 => "3", 21 => "4", 22 => "6", 23 => "5",
        25 => "9", 26 => "7", 28 => "8", 29 => "0",
        31 => "O", 32 => "U", 34 => "I", 35 => "P", 37 => "L", 38 => "J",
        40 => "K", 45 => "N", 46 => "M",
        49 => "Space", 48 => "Tab", 36 => "Return", 53 => "Escape",
        122 => "F1", 120 => "F2", 99 => "F3", 118 => "F4", 96 => "F5",
        97 => "F6", 98 => "F7", 100 => "F8", 101 => "F9", 109 => "F10",
        _ => $"Key {code}"
    };

    public void Dispose() => Stop();

    private const int PollMs = 8;
    private const uint HidSystemState = 1;

    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGEventSourceKeyState(uint stateId, ushort keyCode);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGEventSourceButtonState(uint stateId, uint button);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();
}
