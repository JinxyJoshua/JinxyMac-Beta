# JinxyMac port backlog

What is left to bring across from JinxyClicker (Windows), after the
2026-09-04 click-output / config / wallpaper plan lands.

Requested 2026-09-04 with screenshots of the Windows UI. Each needs its own
spec before implementation — they are features to build, not fixes to port.

## Where the source lives

- Windows app: `C:\Users\rschi\_dev\Joshua\MyBlinkStyleClicker\MyBlinkStyleClicker`
- Mac app: `C:\Users\rschi\_dev\Joshua\JinxyMac`

Read these before porting.

**Macros and the auto switcher share one engine** — they are one spec, not two:

| File | What is in it |
| --- | --- |
| `KeyMacro.cs` | 794 lines — the `KeyMacro` model plus the `MacroRunner` engine |
| `MainWindow.xaml.cs` | switcher UI, ~84 lines; `grep "Switcher"`. Note `const SwitcherName = " AutoSwitcher"` |
| `MainWindow.xaml` | `<StackPanel x:Name="PageMacros">` and `<StackPanel x:Name="PageSwitcher">` |
| `KeyMacro.Tests.cs`, `KeyMacroDwell.Tests.cs`, `KeyMacroWait.Tests.cs` | the existing coverage — port it, do not rewrite it |

**Kit wheel:**

| File | What is in it |
| --- | --- |
| `KitWheel.cs` | 344 lines — roster, rolling, presets, store |
| `MainWindow.KitWheel.cs` | 836 lines — page logic, the reel, art loading |
| `KitImages.cs`, `KitArt.cs`, `KitArtFetch.cs`, `KitArtImage.cs` | art resolution and fetching |
| `MainWindow.xaml` | `<StackPanel x:Name="PageKitWheel">` |
| `Assets/Kits/` | 113 PNGs, 8 MB |
| `KitWheel.Tests.cs`, `KitImages.Tests.cs`, `KitArt.Tests.cs`, `KitArtImage.Tests.cs` | existing coverage |

## Requested next

### Macros — subsystem A
`KeyMacro.cs` (794 lines) plus a new Macros page.

A macro types a key over and over on a timer, into whatever window is in
front. From the screenshots, the page carries:

- A HOTKEYS card with a global "Disable hotkeys" button, and the warning that
  every bound key is live while editing.
- A RUNNING card listing what is active, with "Stop all", and the note that a
  macro goes to the frontmost window so the game must be switched to first.
- Per-macro cards with a name, an interval ("every 10 ms"), a toggle hotkey,
  and per-macro enable/disable — a disabled macro shows a red DISABLED overlay
  across the card.
- A NEW MACRO row: name, key, optional second key (alternates between the two
  when filled), toggle hotkey, interval in ms.

Also in scope: dwell measured in clicks delivered rather than milliseconds,
and the self-measuring hybrid wait. The macro spin-tail fix ports too — the
wait loop's spin tail only ever grew, so a slow sleep busy-spun a whole core;
capped at 2 ms with yields.

### Auto switcher
Swaps between two hotbar slots on a timer — sword and crossbow, or pickaxe and
gumdrop. Fields: first slot, second slot, hold-first ms, equip-delay ms,
hold-second ms. Its own enable toggle and keybind (F1 in the screenshot).

Worth carrying across verbatim: the switcher presses the number keys exactly as
a player would. It does not read the screen or the game.

Note from the Windows history: the switcher is a latch on its own thread, and
every stop that was not its own combined hotkey used to leave it running — it
kept swapping weapons with nothing clicking. It must be cleared in the one
place all stop paths funnel through.

### Kit randomizer — subsystem C
`KitWheel.cs`, `KitImages.cs`, `KitArt*.cs`, `MainWindow.KitWheel.cs`
(~1,875 lines) plus 113 kit images (8 MB).

From the screenshot: a KIT ROLL panel with a large READY?/result display, a
ROLL KIT button, CHALLENGE PROGRESS with "Start a new run", and SAVED WHEELS —
name a set of kits and load it back later.

Roll-without-replacement, reel animation, search, per-kit artwork.

Two open questions this needs to answer:
- **Bundling 8 MB of art into a `.app`** is a packaging decision, not a code
  change. The Windows installer ships them; the Mac bundle has no precedent yet.
- **The kit-art alpha bug may not exist here.** On Windows the wiki served WebP
  under `.png` names and WPF's decoder dropped the alpha, drawing every kit as a
  solid rectangle. Avalonia decodes through Skia and may not share the fault.
  Verify before porting the fix.

### Streamer mode
Hides the "macro on" badge from OBS, Discord screen share and other capture
software, while leaving it visible on the user's own monitor.

**This one may not be achievable.** It depends on `SetWindowDisplayAffinity`,
a Win32 call with no direct macOS equivalent. The badge itself also needs an
Avalonia overlay window, which does not exist yet.

The badge is worth having regardless of whether it can be hidden from capture:
it appears whenever a macro or the switcher is running and names the keys being
sent, so a macro left on does not read as the game misbehaving — several kits
bind the same keys.

## Also outstanding, not requested yet

- **Update-flow changes** — `UpdateCheck`, `UpdateSource`, `UpdateProgressWindow`.
- **Telemetry** — `UsageStats`, `UsagePeriod`, `UsageReporter`, `ReleaseStats`,
  `Server/usage-worker.js`.
- **`NavIcon.cs`** — icons on the nav rail (29 lines).

## Cut on instruction, recorded so they are not mistaken for oversights

- **`ClickDiagnostics`** — the app cannot measure its own timing, and there is
  no dev build on macOS.
- **The background-throttle defence** — App Nap throttles CPU, I/O and timers
  for an occluded app, and Apple Silicon confines background-QoS threads to the
  efficiency cores. Neither is defended against. See the 2026-09-04 design doc's
  "Cut on instruction" section for the evidence and confidence levels.

## Does not port at all

Win32-only, with no macOS equivalent: `timeBeginPeriod`, registry tweaks,
`gdigrab`, and the Tweaks / Optimizations pages entirely.
