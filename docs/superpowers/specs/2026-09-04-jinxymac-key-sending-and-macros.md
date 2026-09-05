# JinxyMac — key sending and the macro engine

Date: 2026-09-04
Status: design, awaiting review
Follows: `2026-09-04-jinxymac-timing-and-click-output-design.md` (merged as 787a803)

## Why this exists

JinxyMac cannot send a keystroke. `IClickEngine` is mouse-only —
`MouseDown`, `MouseUp`, `MoveBy` — and `IHotkeyWatcher` only reads. A macro's
entire job is sending keys, so `KeyMacro.cs` has nothing here to call.

That is the blocker under all of the remaining port. This spec removes it and
then ports the macro engine on top.

## Scope

**In:**

- `IKeyEngine` — a new seam, with Mac and Windows implementations.
- `KeyMacro` — the model: name, key cycle, interval, per-key dwell, equip
  delay, toggle hotkey, per-macro enable.
- `MacroRunner` — the engine that runs them.
- `MacroStore` — persistence.

**Out, each needing its own spec:** the Macros and Switcher pages (this spec
delivers the engine and its tests, not the UI), the kit wheel, and the macro
badge.

**The auto switcher needs no separate work.** It is a `KeyMacro` under the
reserved name `" AutoSwitcher"` — a macro spamming one key is the macro
creator, a macro alternating two is the switcher. Building the general one
gets the specific one for free, and the alternative was two engines that drift
apart. That is the Windows design and it ports unchanged.

## The two findings that shape this

### 1. The event source must be shared, not duplicated

`Engine/MacClickEngine.cs:77` holds a `private static readonly IntPtr Source`,
built once by `CreateSource()`, which does one thing that matters more than
the rest:

```csharp
CGEventSourceSetLocalEventsSuppressionInterval(source, 0.0);
```

An event source suppresses real hardware input for a quarter second after each
post it makes. At 20 CPS a post lands every 50 ms, so that window never closes
and macOS stops seeing its own user — including the hotkey meant to stop the
clicker. Owning the source and setting the interval to zero is what fixed it.

A keyboard engine that creates its own source, or passes `IntPtr.Zero` for the
default one, reintroduces that bug for every key a macro sends. Since macros
are meant to run *while* the clicker runs, the two would compound.

**Decision:** extract the source into `Engine/MacEventSource.cs` — a small
internal static holding the one `IntPtr` and its zeroed suppression interval.
`MacClickEngine` and `MacKeyEngine` both post from it. It is never released:
it lives as long as the process, and there is nowhere sensible to free it that
is not process exit.

### 2. Key codes are not portable, and `macros.json` must say so

`KeyMacro`'s constructor filters `keys.Where(k => k is > 0 and < 256)`. On
Windows those are virtual-key codes. On macOS the app already speaks CGKeyCodes
— `MacHotkeyWatcher` polls `CGEventSourceKeyState(HidSystemState, keyCode)`,
and reserves `MouseBase = 1000` and above for mouse buttons.

Both code spaces pass the `< 256` filter, so a `macros.json` copied from a
Windows install would load without complaint and press **the wrong keys**.

This is a deliberate difference from `ClickHistory`, whose file format was kept
identical across platforms on purpose — "someone who uses both should be able
to copy history.json across and keep their numbers". Macros cannot have that,
because the numbers mean different keys.

**Decision:** `macros.json` gains a `"platform"` field written as `"macos"`.
A file whose platform does not match is refused with the list left empty rather
than loaded — a macro pressing an unintended key in a game is worse than a
macro that is missing. The refusal is silent in the same way every other load
failure here is silent: fall back to the default, never crash.

The key-code filter stays `> 0 and < 256` for ordinary keys, and `MouseBase`
and above stay reserved, matching `MacHotkeyWatcher`.

## Architecture

### New files

| File | What it holds |
| --- | --- |
| `Engine/IKeyEngine.cs` | the seam: `IsAvailable`, `Unavailable`, `KeyDown(int)`, `KeyUp(int)` |
| `Engine/MacEventSource.cs` | the shared Quartz source, suppression interval zero |
| `Engine/MacKeyEngine.cs` | `CGEventCreateKeyboardEvent` + `CGEventPost` |
| `Engine/WindowsKeyEngine.cs` | `SendInput` with `KEYBDINPUT`, scan code via `MapVirtualKey` |
| `Core/KeyMacro.cs` | the model plus `MacroRunner` plus `MacroStore` |
| `Core/KeyMacro.Tests.cs`, `Core/KeyMacroDwell.Tests.cs`, `Core/KeyMacroWait.Tests.cs` | ported from Windows |

`IKeyEngine` is deliberately down/up rather than a single `Tap`. The dwell
between them is the macro's business — the switcher holds a slot for hundreds
of milliseconds — and an engine that owned the timing could not express that.

### Changed files

| File | Change |
| --- | --- |
| `Engine/MacClickEngine.cs` | posts from `MacEventSource.Handle` instead of its own private field |
| `MainWindow.axaml.cs` | constructs the key engine beside the click engine; owns a `MacroRunner` |
| `Testing/JinxyMac.Tests.csproj` | the new files, by hand as always |

### What does not come across from `KeyMacro.cs`

- **`TimeBeginPeriod` / `TimeEndPeriod`** (winmm.dll). Win32, and the macOS
  equivalent question was already settled: the background-throttle defence was
  cut on instruction in the previous spec. Removed, not replaced.
- **`SendInput`, `MapVirtualKey`, and the `INPUT`/`KEYBDINPUT`/`MOUSEINPUT`
  structs.** They move into `WindowsKeyEngine`, which is where they belong.
- The vestigial `MOUSEINPUT` declaration, which the Windows file keeps only to
  size the union correctly, moves with them and keeps its comment.

### What comes across unchanged, and must

- **`MaxSpinTailMs = 2.0`** in `Wait`. The spin tail only ever grew, so a slow
  sleep busy-spun a whole core forever. The cap and the yield are the fix.
- **`InputGate`** — the runner shares the clicker's lock, so a key cannot land
  between a mouse press and its release and turn the click into a drag.
- **`Suppressed`** and **`Clicks`** hooks — dwell measured in clicks delivered
  rather than milliseconds depends on the second one.
- **`MinimumDwellMs`** and the equip delay. A weapon takes time to appear, and
  clicks before it does are wasted.

## Error handling

Unchanged in character: nothing here may take the app down.

- A failed key post is caught and dropped, exactly as `MacClickEngine.Post`
  does. A macro that misses a keystroke keeps running.
- `MacroStore` load failures produce an empty list, never an exception.
- `MacroRunner.Dispose` stops every running macro; a macro thread that throws
  releases any key it was holding, in a `finally`, for the same reason the
  click loop does.

## Testing

The three Windows test files port directly — they cover the model, the dwell
arithmetic, and the wait loop, and none of them need a Mac. Added to those:

| Suite | Covers |
| --- | --- |
| `KeyMacro.Tests` | construction, key filtering, clamping, `IsUsable`, summary text |
| `KeyMacroDwell.Tests` | `DwellFor`, `MinimumDwellMs`, the equip delay |
| `KeyMacroWait.Tests` | the wait loop and the capped spin tail |
| `MacroStore.Tests` (new) | the platform field: a `"windows"` file is refused, a `"macos"` file loads, a missing field is refused |
| `MacroRunner.Tests` (new) | start/stop, `StopAll`, every key released on stop — the same fake-engine pairing walk that caught the stuck-button bug |

That last one matters for the same reason it did for the mouse: a macro
stopped mid-press must release the key it was holding, or the game sees a key
held down with nothing to lift it.

## Version

`Core/Updater.cs` `Version` goes `1.1.0` → `1.2.0` when the UI lands. This
spec delivers the engine only and does not bump it.
