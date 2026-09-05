# Key Sending and Macro Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give JinxyMac the ability to send keystrokes, then port JinxyClicker's macro engine — which brings the auto switcher with it.

**Architecture:** A new `IKeyEngine` seam beside the existing `IClickEngine`, with Mac and Windows implementations, so the whole thing still runs on a PC. The Quartz event source moves out of `MacClickEngine` into a shared holder, because both engines must post from the one source whose suppression interval is zeroed. `KeyMacro.cs` then ports with its Win32 surface — about 60 of its 803 lines — replaced by calls through the seam.

**Tech Stack:** C#, .NET 10 (`net10.0`), Avalonia 11.3, xunit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-09-04-jinxymac-key-sending-and-macros.md`

**Source being ported:** `.superpowers/port-source/KeyMacro.cs` (803 lines, extracted from the Windows app). Read it directly — it is the reference, and its doc comments carry measurements that must survive.

## Global Constraints

- Namespaces: `JinxyMac.Core` for the model and runner, `JinxyMac.Engine` for the seam. Tests in `JinxyMac.Core.Tests`.
- `Core/` must contain NO Avalonia or UI-framework dependency. It may reference `JinxyMac.Engine`.
- `Testing/JinxyMac.Tests.csproj` sets `EnableDefaultCompileItems=false` — every new file is added by hand, appended, never disturbing existing entries.
- Target `net10.0`, no `-windows` suffix, never WPF.
- Build must stay at **0 warnings**. Current baseline: **197 tests passing**.
- Nothing may crash the app. Every P/Invoke and file operation is wrapped and falls back.
- Commit messages end with: `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`

## The conversion table

Applies throughout. Anything not in this table ports unchanged.

| Windows | macOS |
| --- | --- |
| `SendInput` with `KEYBDINPUT` | `CGEventCreateKeyboardEvent(source, keyCode, down)` then `CGEventPost(0, event)` then `CFRelease` |
| `MapVirtualKey(vk, MAPVK_VK_TO_VSC)` | deleted — a CGKeyCode already *is* the hardware code |
| `INPUT` / `InputUnion` / `MOUSEINPUT` | deleted, along with the union-sizing comment |
| `timeBeginPeriod` / `timeEndPeriod` | deleted |
| `ProcessTiming.KeepResponsiveInBackground()` | deleted |
| Windows virtual-key codes | macOS CGKeyCodes — same `0 < k < 256` filter, different meaning |

---

### Task 1: The key-sending seam

**Files:**
- Create: `Engine/IKeyEngine.cs`
- Create: `Engine/MacEventSource.cs`
- Create: `Engine/MacKeyEngine.cs`
- Create: `Engine/WindowsKeyEngine.cs`
- Modify: `Engine/MacClickEngine.cs` (post from the shared source)

**Interfaces:**
- Consumes: nothing
- Produces:
  - `IKeyEngine` with `bool IsAvailable`, `string? Unavailable`, `void KeyDown(int code)`, `void KeyUp(int code)`
  - `MacEventSource.Handle -> IntPtr`

- [ ] **Step 1: Write the interface**

Create `Engine/IKeyEngine.cs`:

```csharp
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
```

- [ ] **Step 2: Extract the shared event source**

Create `Engine/MacEventSource.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JinxyMac.Engine;

/// <summary>
/// The one Quartz event source every synthetic event in this app is posted
/// from.
/// </summary>
/// <remarks>
/// Shared rather than one per engine, for the reason the clicker found the
/// hard way: a source has a local events suppression interval, and it
/// defaults to a quarter of a second. For that long after each synthetic
/// event macOS ignores the real mouse and keyboard.
///
/// A quarter second is nothing when a script clicks once. The clicker posts
/// every fifty milliseconds at twenty CPS, so the window never closes and the
/// machine stops seeing its own user — including the hotkey meant to stop it.
/// Setting the interval to zero is the whole point of owning the source.
///
/// A second source created for the keyboard would carry the default interval
/// and reintroduce exactly that, for every key a macro sends — and macros are
/// meant to run while the clicker runs, so the two would compound.
/// </remarks>
[SupportedOSPlatform("macos")]
public static class MacEventSource
{
    /// <summary>
    /// The source, or <see cref="IntPtr.Zero"/> if it could not be made.
    /// </summary>
    /// <remarks>
    /// Zero is a valid argument to every function that takes it — it just
    /// means the default source, suppression interval and all. Worse, but
    /// still working, which is the right failure for this.
    ///
    /// Never released: it lives as long as the process, and there is nowhere
    /// sensible to free it that is not process exit.
    /// </remarks>
    public static IntPtr Handle { get; } = Create();

    private static IntPtr Create()
    {
        try
        {
            IntPtr source = CGEventSourceCreate(SourceStateHidSystem);

            if (source != IntPtr.Zero) CGEventSourceSetLocalEventsSuppressionInterval(source, 0.0);

            return source;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>Hardware state, the same source the real mouse reports through.</summary>
    private const uint SourceStateHidSystem = 1;

    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventSourceCreate(uint stateId);

    [DllImport(ApplicationServices)]
    private static extern void CGEventSourceSetLocalEventsSuppressionInterval(IntPtr source, double seconds);
}
```

- [ ] **Step 3: Point MacClickEngine at the shared source**

In `Engine/MacClickEngine.cs`, delete the `Source` field, the `CreateSource()` method, the `SourceStateHidSystem` const, and the `CGEventSourceCreate` / `CGEventSourceSetLocalEventsSuppressionInterval` declarations. Replace every use of `Source` with `MacEventSource.Handle`.

Leave everything else in that file exactly as it is — in particular `CGEventSetIntegerValueField(click, EventFieldClickState, 1)` and the pressure line, which are what made the clicks register in Roblox.

- [ ] **Step 4: Write the macOS key engine**

Create `Engine/MacKeyEngine.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JinxyMac.Engine;

/// <summary>
/// Keystrokes on macOS, through Quartz.
/// </summary>
/// <remarks>
/// Simpler than the mouse side, because macOS has no equivalent of Windows'
/// split between a virtual key and a scan code. On Windows a key event has to
/// carry both — games routinely read the scan code and ignore an event
/// carrying only the virtual key. A CGKeyCode already is the hardware code,
/// so there is nothing second to carry and nothing to translate.
///
/// Posted to the HID tap and from the shared source, for the same reasons the
/// clicks are: lowest point injection is possible from, and an interval that
/// does not blind macOS to the real keyboard.
///
/// Needs Accessibility permission like everything else here. Without it macOS
/// accepts the call and throws the event away.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacKeyEngine : IKeyEngine
{
    public bool IsAvailable => OperatingSystem.IsMacOS() && IsTrusted();

    public string? Unavailable
    {
        get
        {
            if (!OperatingSystem.IsMacOS()) return "Not running on macOS.";

            return IsTrusted()
                ? null
                : "Accessibility permission is not granted, so macOS is discarding "
                  + "every keystroke. Grant it under System Settings, Privacy & Security, "
                  + "Accessibility, then restart JinxyMac.";
        }
    }

    public void KeyDown(int code) => Post(code, down: true);

    public void KeyUp(int code) => Post(code, down: false);

    private static void Post(int code, bool down)
    {
        if (code <= 0 || code > ushort.MaxValue) return;

        IntPtr key = IntPtr.Zero;

        try
        {
            key = CGEventCreateKeyboardEvent(MacEventSource.Handle, (ushort)code, down);
            if (key == IntPtr.Zero) return;

            CGEventPost(HidEventTap, key);
        }
        catch
        {
            // A failed key must never take a macro thread — or the app — down.
        }
        finally
        {
            if (key != IntPtr.Zero) CFRelease(key);
        }
    }

    private static bool IsTrusted()
    {
        try
        {
            return AXIsProcessTrusted();
        }
        catch
        {
            return false;
        }
    }

    private const uint HidEventTap = 0;

    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreateKeyboardEvent(
        IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(ApplicationServices)]
    private static extern void CGEventPost(uint tap, IntPtr theEvent);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr reference);
}
```

- [ ] **Step 5: Write the Windows key engine**

Create `Engine/WindowsKeyEngine.cs`. Carry the `INPUT` / `InputUnion` / `KEYBDINPUT` / `MOUSEINPUT` structs and the `SendInput` / `MapVirtualKey` declarations across from `.superpowers/port-source/KeyMacro.cs` lines 597-647 **verbatim**, including the `MOUSEINPUT` remark explaining that the unused member is load-bearing for the union's size. Class doc comment mirrors `WindowsClickEngine`'s: it exists so the app can be run and judged without a Mac.

```csharp
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
```

`IsAvailable` is `OperatingSystem.IsWindows()`; `Unavailable` mirrors `WindowsClickEngine`.

- [ ] **Step 6: Build and verify**

Run: `dotnet build -c Debug -v minimal`
Expected: `0 Warning(s) 0 Error(s)`

Run: `dotnet test Testing/JinxyMac.Tests.csproj`
Expected: 197 passing — this task adds no tests and must break none.

- [ ] **Step 7: Commit**

```bash
git add Engine/IKeyEngine.cs Engine/MacEventSource.cs Engine/MacKeyEngine.cs Engine/WindowsKeyEngine.cs Engine/MacClickEngine.cs
git commit -m "$(cat <<'EOF'
Let the app send keys, not only clicks

Nothing here could type. IClickEngine sends mouse input and the hotkey
watcher only reads, so the macro engine had nothing to call.

The Quartz event source moves out of the click engine and is shared. That
source exists to set its local events suppression interval to zero: a source
blinds macOS to the real mouse and keyboard for a quarter second after each
post, and at twenty clicks a second that window never closes. A second source
for the keyboard would carry the default interval and bring it back for every
key a macro sends - and macros are meant to run while the clicker runs, so
the two would compound.

Simpler than the mouse side. Windows has to carry a scan code beside the
virtual key because games read the former and ignore an event with only the
latter; a CGKeyCode already is the hardware code, so there is nothing second
to send and nothing to translate.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: The macro model and its store

**Files:**
- Create: `Core/KeyMacro.cs` (the `KeyMacro` class and `MacroStore` only — `MacroRunner` comes in Task 3)
- Create: `Core/KeyMacro.Tests.cs`, `Core/KeyMacroDwell.Tests.cs`
- Modify: `Testing/JinxyMac.Tests.csproj`

**Interfaces:**
- Consumes: `SettingsPath.For(string)`; `HotkeyBinding` (check whether it already exists in this repo — if not, port it from the Windows source alongside)
- Produces:
  - `KeyMacro` with `Name`, `Keys`, `KeysText`, `IntervalMs`, `HoldsMs`, `ClicksWanted`, `EquipMs`, `Hotkey`, `Enabled`, `DwellFor(int)`, `IsUsable`, `RateText`, `SummaryText`
  - `KeyMacro.MinIntervalMs = 5`, `MaxIntervalMs = 10_000`, `DefaultEquipMs = 60`
  - `KeyMacro.MinimumDwellMs(double clickPeriodMs, int equipMs, int clicks)`
  - `MacroStore.Load()`, `MacroStore.Save(IEnumerable<KeyMacro>)`, `MacroStore.Defaults()`

- [ ] **Step 1: Check what already exists**

```bash
cd /c/Users/rschi/_dev/Joshua/JinxyMac
grep -rn 'HotkeyBinding' Core/ Engine/ MainWindow.axaml.cs | head
```

`HotkeyBinding` is a dependency of `KeyMacro`. If this repo has one, use it. If not, port it from the Windows app at `C:\Users\rschi\_dev\Joshua\MyBlinkStyleClicker\MyBlinkStyleClicker\HotkeySettings.cs` and note it in the report.

- [ ] **Step 2: Port the model**

Copy `KeyMacro` (lines 28-186 of `.superpowers/port-source/KeyMacro.cs`) into `Core/KeyMacro.cs`. Namespace becomes `JinxyMac.Core`. Every doc comment comes across verbatim — they record measured behaviour and the reasons behind the defaults.

One addition, to the `Keys` filter's doc comment: note that these are the platform's own key codes — CGKeyCodes here, virtual-key codes on Windows — and that both spaces pass the same `0 < k < 256` filter while meaning different keys.

- [ ] **Step 3: Port the store, with a platform guard**

Copy `MacroStore` (lines 653-end) into the same file. Then add the guard the spec requires: `macros.json` carries `"platform": "macos"`, and a file whose platform is absent or different loads as an empty list.

```csharp
    /// <summary>What this build's key codes mean.</summary>
    /// <remarks>
    /// Written into the file and checked on the way back in. KeyMacro's key
    /// filter accepts 0-255 on both platforms, but those are CGKeyCodes here
    /// and virtual-key codes on Windows — a file carried across would load
    /// without complaint and press entirely different keys.
    ///
    /// Deliberately unlike history.json, which was kept identical across
    /// platforms on purpose so someone using both could copy their numbers
    /// over. Numbers mean the same thing everywhere; key codes do not.
    ///
    /// A mismatch loads as no macros rather than as an error. A macro pressing
    /// an unintended key in a game is worse than a macro that is missing.
    /// </remarks>
    private const string PlatformTag = "macos";
```

Store the file as an object with `platform` and `macros` members rather than a bare array.

- [ ] **Step 4: Port the two model test files**

Copy `KeyMacro.Tests.cs` and `KeyMacroDwell.Tests.cs` from the Windows app (`C:\Users\rschi\_dev\Joshua\MyBlinkStyleClicker\MyBlinkStyleClicker\`), changing only the namespace. Do not weaken any assertion. If one fails, read why before touching it — the model is meant to be unchanged.

Add a new `MacroStore` test covering the platform guard: a `"macos"` file loads its macros, a `"windows"` file loads empty, a file with no platform member loads empty.

- [ ] **Step 5: Add every new file to the test project**

```xml
    <Compile Include="../Core/KeyMacro.cs" />
    <Compile Include="../Core/KeyMacro.Tests.cs" />
    <Compile Include="../Core/KeyMacroDwell.Tests.cs" />
```

Plus `HotkeySettings.cs` if it had to be ported, and `../Engine/IKeyEngine.cs` if the model references it (it should not — only the runner does).

- [ ] **Step 6: Run the tests**

Run: `dotnet test Testing/JinxyMac.Tests.csproj`
Expected: 197 plus the ported cases, all passing, build 0 warnings.

- [ ] **Step 7: Commit**

```bash
git commit -m "$(cat <<'EOF'
Port the macro model, and stop a Windows macros.json loading here

A macro is a key, or a short cycle of keys, sent over and over on a timer.
One shape covers both features people ask for: spam one key and it is the
macro creator, alternate two and it is the inventory switcher.

macros.json now records which platform wrote it, and a file from the other
one loads as no macros at all. The key filter accepts 0-255 on both, but
those are CGKeyCodes here and virtual-key codes there - a file carried across
would load without complaint and press entirely different keys. That is
deliberately unlike history.json, which was kept identical across platforms
so someone using both could copy their numbers over; numbers mean the same
thing everywhere and key codes do not.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: The runner

**Files:**
- Modify: `Core/KeyMacro.cs` (add `MacroRunner`)
- Create: `Core/KeyMacroWait.Tests.cs`, `Core/MacroRunner.Tests.cs`
- Modify: `Testing/JinxyMac.Tests.csproj`

**Interfaces:**
- Consumes: `IKeyEngine` (Task 1), `KeyMacro` (Task 2)
- Produces: `MacroRunner(IKeyEngine engine)` with `Start(KeyMacro)`, `Stop(string)`, `StopAll()`, `IsRunning(string)`, `RunningCount`, `Sent`, `Changed`, `Suppressed`, `InputGate`, `Clicks`, `Dispose()`

- [ ] **Step 1: Port MacroRunner with the conversion applied**

Copy `MacroRunner` (lines 188-648) into `Core/KeyMacro.cs`. Apply the conversion table:

- Constructor takes an `IKeyEngine` and stores it. (Windows has no constructor; add one.)
- `Loop`: delete the `ProcessTiming.KeepResponsiveInBackground()` call, the `TimeBeginPeriod`/`TimeEndPeriod` calls, the `raisedTimer` local, and the `finally` that used it. Keep the `try`/`catch` — a failed send must not take the thread.
- `SendGated`: delete the `scan` local and the `MapVirtualKey` call. Becomes:

```csharp
    private void SendGated(int code, CancellationToken token)
    {
        Gated(() => _engine.KeyDown(code));

        // Outside the gate. The clicker is free to click while a key is held —
        // that is just clicking with a key down, which is what a hand does.
        Wait(HoldMs, token);

        Gated(() => _engine.KeyUp(code));
    }
```

- Delete `Press` entirely if nothing calls it after the change; if something does, convert it the same way.
- Delete `Send`, `InputKeyboard`, `KeyEventUp`, `MapVkToVsc`, the four structs, and both `DllImport`s — they live in `WindowsKeyEngine` now.
- Everything else — `Start`, `Stop`, `StopAll`, `Changed`, `Wait`, `WaitForShots`, `Gated`, `Dispose`, and every constant including `MaxSpinTailMs = 2.0` and `SpinTailMs = 1.2` — comes across **verbatim, doc comments included**.

The comment on `Loop` explaining the timer opt-out refers to Windows behaviour that no longer applies. Replace it with one sentence saying the timer work was Windows-only and is not carried across, rather than deleting it silently.

- [ ] **Step 2: Add a release guarantee to the loop**

The Windows loop has no `finally` releasing a held key, because `SendGated` always pairs its own down and up. But a cancellation observed inside `Wait(HoldMs, token)` returns early and the `KeyUp` still runs — confirm that by reading, and if a path exists where a key is left down, add a `finally` that releases it, matching what `Clicker.Loop` does for the mouse button. State in the report which it was.

- [ ] **Step 3: Port the wait tests**

Copy `KeyMacroWait.Tests.cs` from the Windows app, namespace only. It covers the capped spin tail — the fix for a macro busy-spinning a whole core.

- [ ] **Step 4: Write runner tests with a fake engine**

Create `Core/MacroRunner.Tests.cs` with a `FakeKeyEngine` recording `(code, down)` pairs, in the shape of `Core/Clicker.Tests.cs`'s `FakeClickEngine` — read that file and match it.

Cover:
- a started macro sends its key
- a two-key macro alternates
- `Stop` ends it and every key that went down came back up
- `StopAll` with several running
- a disabled macro refuses to start
- an unusable macro (no keys, or a blank name) refuses to start
- stopping mid-press still releases the key — the same walk that caught the stuck mouse button

- [ ] **Step 5: Add the files to the test project and run**

Run: `dotnet test Testing/JinxyMac.Tests.csproj`
Expected: all passing, 0 warnings. Run the runner tests 5 times consecutively — they are threaded and flakiness is a real risk.

- [ ] **Step 6: Commit**

```bash
git commit -m "$(cat <<'EOF'
Port the macro runner, sending through the engine seam

The scheduling comes across unchanged, including the two corrections that
were found by measurement. The wait loop sleeps the bulk and spins the last
stretch, because a dwell built from short sleeps overshoots - a 150ms dip
made of thirty 5ms sleeps measured 165-180ms. And the spin tail is capped at
2ms: it only ever grew, so one slow sleep set it high and from then on every
shorter interval was spun end to end, burning a whole core for as long as the
macro ran. That was the in-game stutter.

The key is pressed and released around the clicker's gate but not across the
hold between them. Held for the whole press it would block the click loop
every swap - a quarter of all clicks at the rates this technique wants. A
feature that costs hits in a game measured in hits is worse than no feature.

The Windows timer-resolution work is not carried across; it was Win32 and the
macOS equivalent was ruled out of scope.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Construct the engines in the window

Wiring only — no UI. The Macros and Switcher pages are a separate spec.

**Files:**
- Modify: `MainWindow.axaml.cs`

**Interfaces:**
- Consumes: `IKeyEngine`, `MacKeyEngine`, `WindowsKeyEngine`, `MacroRunner`
- Produces: a live `MacroRunner` field with `InputGate` and `Clicks` wired

- [ ] **Step 1: Build the key engine beside the click engine**

Find where `_engine` is chosen in the constructor — it is an `if`/`else` on the platform, written that way so the compiler can see the checks. Add the key engine in the same shape, and store it.

- [ ] **Step 2: Construct the runner and wire its hooks**

```csharp
        _macros = new MacroRunner(_keys)
        {
            // Shares the clicker's gate, so a key cannot land between a mouse
            // press and its release and turn the click into a drag.
            InputGate = _clicker.InputGate,

            // Lets a dip end when the weapon has actually fired rather than
            // when a stopwatch says it probably has.
            Clicks = () => _clicker.ClickCount
        };
```

- [ ] **Step 3: Dispose it with the others**

In the `Closed` handler, add `_macros.Dispose();` beside `_clicker.Dispose();`.

- [ ] **Step 4: Build, test, and run the app**

```bash
dotnet build -c Debug -v minimal
dotnet test Testing/JinxyMac.Tests.csproj
./bin/Debug/net10.0/JinxyMac.exe
```

Expected: 0 warnings, all tests passing, and the app opens and closes cleanly. Do not claim any visual behaviour — there is no macro UI yet, so there is nothing new to see. The controller will confirm the window still renders.

- [ ] **Step 5: Commit**

```bash
git commit -m "$(cat <<'EOF'
Give the window a macro runner

Engine and runner only - there is no macro UI yet, so nothing is visible.
The runner shares the clicker's input gate so a key cannot land between a
mouse press and its release and turn the click into a drag, and reads the
clicker's delivered count so a weapon dip can end on the shot that fires
rather than on a timer generous enough to be safe.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## What this plan does not do

- **No Macros page, no Switcher page.** The engine and its tests only. The UI is a separate spec — it is the larger half, and the screenshots show a page with per-macro cards, a running list, a master hotkey switch, and a new-macro row.
- **No kit wheel** — 1,924 lines across five files, plus the 8 MB art bundling decision.
- **No badge.** `MacroBadge.cs` states on its own line 3 that `SetWindowDisplayAffinity` has no macOS equivalent, and it is WPF throughout. It needs writing from scratch as an Avalonia overlay, and the hide-from-capture half may not be achievable at all.
