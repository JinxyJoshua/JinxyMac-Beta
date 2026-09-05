using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JinxyMac.Engine;

/// <summary>
/// Global hotkeys on Windows, so binding and rebinding can be exercised here.
/// </summary>
/// <remarks>
/// Not shipped. It exists so the hotkey flow — bind, rebind, fire from another
/// window — can be tried on the machine this is being written on, rather than
/// discovered to be wrong by someone with a Mac.
///
/// Polls GetAsyncKeyState on the same cadence as the macOS watcher, so the two
/// behave alike and a timing bug found here is a timing bug there.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsHotkeyWatcher : IHotkeyWatcher
{
    private CancellationTokenSource? _cts;
    private Action<int, string>? _capture;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public string? Unavailable => OperatingSystem.IsWindows() ? null : "Not running on Windows.";

    /// <summary>Swapped as a whole, so the poll thread never reads a half-built set.</summary>
    private volatile int[] _watched = Array.Empty<int>();

    public event Action<int>? Pressed;

    public event Action<int>? Released;

    // Deliberately still > 0, unlike the macOS watcher's Bindable: virtual-key
    // code 0 is not a key on Windows, and KeyCodes.For never produces it on
    // this platform, so there is no A-shaped reason to admit it here.
    public void Watch(IEnumerable<int> codes) =>
        _watched = codes.Where(c => c is > 0 and < 256).Distinct().ToArray();

    public void CaptureNext(Action<int, string> captured) => _capture = captured;

    public void Start()
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        new Thread(() => Poll(token))
        {
            IsBackground = true,
            Name = "WindowsHotkeys"
        }.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private void Poll(CancellationToken token)
    {
        var down = new bool[256];

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_capture is { } capture)
                {
                    for (int code = 8; code < down.Length; code++)
                    {
                        // Skip the mouse buttons this app is busy synthesising.
                        if (code is 1 or 2 or 4) continue;

                        bool held = IsDown(code);

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
                        bool held = IsDown(code);

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

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private static string Name(int code) => code switch
    {
        >= 0x41 and <= 0x5A => ((char)code).ToString(),
        >= 0x30 and <= 0x39 => ((char)code).ToString(),
        >= 0x70 and <= 0x7B => "F" + (code - 0x6F),
        0x20 => "Space",
        0x09 => "Tab",
        0x0D => "Return",
        0x1B => "Escape",
        0x05 => "Mouse 4",
        0x06 => "Mouse 5",
        _ => $"Key {code}"
    };

    public void Dispose() => Stop();

    private const int PollMs = 8;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
