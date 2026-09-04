# JinxyMac 1.1.0 — timing integrity, click output, wallpaper

Date: 2026-09-04
Status: design, awaiting review

## Why this exists

JinxyClicker (Windows) moved 18 commits between 2026-08-23 and 2026-09-02.
JinxyMac has none of it. This spec covers the first of several ports, chosen
because it is the work that makes the Mac clicker click well — the reason the
app exists.

### Correcting the framing this started from

The port was described as a list of small fixes that "port cleanly": the
per-click allocation fix, the CPS advice correction, kit search, per-hotkey
unbind, the kit-art alpha bug, the first-run kit list.

That list does not port. Every item on it except the allocation fix lives
inside a subsystem JinxyMac does not have:

| Item | Lives in | Mac status |
| --- | --- | --- |
| kit search | `MainWindow.KitWheel.cs:203` | no kit wheel |
| kit-art alpha bug, first-run kit list | `KitArt*.cs` | no kit art |
| per-hotkey unbind | `MainWindow.xaml.cs:4649` | no macro system |
| CPS advice correction | `ClickOutput.cs:78` | no `ClickOutput.cs` |
| per-click allocation fix | `ClickOutput`/`ClickTiming` in `1b34a60` | partial |

Windows has 14 nav pages; the Mac has 6. There is no Macros page, no Kit Wheel
page, no Switcher page. Those are features to be built, not bugs to be fixed.

The real scope of the 18 commits is roughly 4,200 lines of new non-test source
across seven independent subsystems. This spec takes three of them.

## Scope

**In:**

- **F — timing integrity.** `ClickDiagnostics`, the background-throttle
  defence, `RemoteConfig`.
- **B — click output.** `ClickOutput`, `ClickButton` (right and wheel), the
  `Measured` preset, the corrected CPS advice.
- **G — wallpaper.** `Wallpaper.cs` and a picker on the Theme page.

**Out, each needing its own spec:** macros (subsystem A), kit wheel and kit art
(C), update-flow changes (D), telemetry (E), dev tools (H, excluded by
instruction), and the App Nap opt-out (see "Deferred" below).

## Decisions taken before this design

### The five duplicated files are not shared

The two repos were to be de-duplicated by sharing the portable files once,
on the reasoning that sharing stops the drift recurring. That is rejected.

Only **two of five** files are actually shareable:

| File | Win LOC | Mac LOC | Diff lines | Verdict |
| --- | --- | --- | --- | --- |
| `ClickTiming` | 129 | 106 | 35 | shareable in principle |
| `ClickHistory` | 155 | 140 | 70 | shareable in principle |
| `AppSettings` | 176 | 111 | 289 | diverged, legitimately |
| `Updater` | 163 | 286 | 451 | diverged — Mac's is **bigger** |
| `Presets` | — | 170 | n/a | does not exist on Windows |

`Presets.cs` has no Windows counterpart; Windows has `ClickPreset.cs`, a
different 206-line file. The Mac `Updater` is larger because it performs
detached bundle-swapping, which Windows has no analogue for.

The mechanism is also worse than it appears. JinxyMac-Beta is a separate public
repository that must clone and build standalone. A
`<Compile Include="../../MyBlinkStyleClicker/..." />` link works only because
the two repos happen to be siblings on one developer's disk — that is a
property of that machine, not of the projects. Anyone cloning JinxyMac-Beta
alone gets a build error. A NuGet package or git submodule fixes that but adds
a feed, versioning ceremony, and a release step in order to change one
constant, for 235 lines of code.

Finally, `ClickTiming` is diverging **deliberately**: this spec routes the
HitFix floors through `RemoteConfig`, exactly as Windows now does. The two
files converge as a consequence of both taking the same design, not as a
consequence of being the same file.

**Decision:** port file-by-file. If drift protection is wanted later, a test
that diffs the two files modulo namespace is far cheaper than coupling the
builds.

### macOS has both of the throttles Windows has

The Windows `ProcessTiming.cs` was assessed as Win32-only, needing "a macOS
equivalent or nothing". That assessment is wrong, and it matters, because it
is most of subsystem F.

- **App Nap** applies CPU priority lowering, I/O priority lowering, and timer
  throttling to apps the system judges inactive. JinxyMac's window is always
  occluded in real use — the game is in front — so it is squarely in scope.
  This is the `timeBeginPeriod` half. (confidence: high)
- **Thread QoS.** On Apple Silicon, threads at background QoS are confined to
  efficiency cores. This is EcoQoS near-exactly: not applied at launch, applied
  as the system's judgement of the process shifts over a session. That is the
  precise shape of "fine for two hours, then the hit registration gets worse".
  (confidence: high)

JinxyMac currently has no defence against either. `Clicker.Start()` sets
`ThreadPriority.AboveNormal`, which .NET maps onto POSIX nice values; that does
not prevent QoS-based efficiency-core confinement. (confidence: moderate)

**Decision:** implement the thread-QoS and GC halves. Defer App Nap.

### Diagnostics get a face on the Mac

On Windows the timing readout lives in the DEV tab, which is out of scope here
and has no Mac equivalent. The deciding constraint is that the developer has no
Mac: when a user reports degradation, numbers they can read off the screen are
the only evidence channel that exists.

**Decision:** `ClickOutput`'s verdict sentence is always visible, matching the
Windows public surface. The numbers live in a collapsed "Timing detail"
expander with a Copy button that puts the whole readout on the clipboard as
text. Asking a user to transcribe six numbers by hand is where evidence dies.

### RemoteConfig ships, trimmed

The case is stronger on Mac than on Windows. A release costs Mac users real
pain: the bundle is replaced, the app is unsigned, so its Accessibility entry
goes stale and must be removed and re-added by hand in System Settings.
Correcting a timing floor by editing one file in a browser skips all of that.
`RecorderEnabled` also matters more here, since ffmpeg and Screen Recording
have already produced two real bugs on hardware that cannot be tested.

**Decision:** ship it with four fields. Drop `macroEquipMs` and
`kitArtFetchEnabled` — the Mac has neither feature, and dead keys in a file
whose entire value is being trustworthy are a small lie.

## Architecture

### New files

#### `Core/ClickDiagnostics.cs`

Verbatim port; namespace change only. `Stopwatch` plus a `long[512]` ring
buffer with a lock around a cheap swap. No platform surface at all.

Constants carried across unchanged: `Window = 512`, `StallFactor = 3.0`.
Summary uses median rather than mean throughout, and reports
`(Samples, DeliveredCps, MedianMs, WorstMs, JitterMs, Stalls)`.

#### `Core/ClickOutput.cs`

Verbatim port; namespace change only. Pure arithmetic and strings.

Carries the corrected advice: `DiminishingReturnsCps = 36.0` (down from 50),
`MeasuredBestCps = 33.3`, `MismatchTolerance = 0.15`. The doc comment
explaining why 8–12 CPS community advice was wrong — it described Minecraft's
Bedwars, a different game on a different server — comes across intact. It is
load-bearing: the mistake is easy to repeat.

#### `Core/ClickButton.cs`

**Split from the Windows file.** The `ClickButton` enum (`Left`, `Right`,
`Middle`) and the `Label`/`Parse` helpers are portable and come across.
`Label` keeps the Windows wording, where `Middle` displays as "Wheel".

The `ClickButtons` Win32 flag table does **not** come across. Its platform
equivalents live in the engine implementations.

`Parse` keeps its "anything unrecognised is Left" behaviour: a hand-edited
settings file naming a button that does not exist should leave a working
clicker, not one that presses nothing.

#### `Core/RemoteConfig.cs`

Trimmed port. Four fields:

| Key | Type | Bounds | Default |
| --- | --- | --- | --- |
| `hitFixMinDownMs` | double | 1–100 | 15.0 |
| `hitFixMinUpMs` | double | 1–100 | 15.0 |
| `recorderEnabled` | bool | — | true |
| `notice` | string | ≤200 chars | "" |

The invariants that make this safe are carried across unchanged, and are not
negotiable:

- Every value is clamped **by the app**, never by the file. A hostile or
  mistaken config can only choose a value the app would already have accepted
  from its own settings screen.
- It can move numbers within shipped bounds and turn features off. It cannot
  change behaviour. Anything able to would be downloading and running new code
  on other people's machines.
- Everything fails to the shipped defaults: no network, bad JSON, wrong type,
  out of range, unknown key.
- `IsTrusted` pins the host to `raw.githubusercontent.com` over HTTPS.
- `Notice` is only ever displayed as text — never parsed, never used as an
  address, never run.

`Updater` currently holds a private `Feed` const at `Core/Updater.cs:40`. This
adds `public const string Owner = "JinxyJoshua"` and
`public const string Repo = "JinxyMac-Beta"`, rebuilds `Feed` from them, and
has `RemoteConfig.Url` derive from the same pair, so the repository is named
once.

A `config.json` is committed at the root of JinxyMac-Beta, carrying the same
self-documenting `_comment` keys the Windows one uses.

#### `Core/Wallpaper.cs`

Port. The logic — copy the chosen file into the settings folder, store a bare
name rather than a path — is fully portable and has no Win32 in it. Two
changes:

- `FileFilter`, a WPF filter string, is replaced by an Avalonia
  `FilePickerFileType` for `IStorageProvider.OpenFilePickerAsync`.
- The `Allowed` list keeps `.png .jpg .jpeg .bmp .webp`. Avalonia decodes
  through SkiaSharp, which handles all five. (confidence: moderate — verify
  `.webp` decodes before relying on it; if it does not, drop it from the list
  rather than shipping a format that fails at load.)

#### `Engine/IProcessTiming.cs`, `MacProcessTiming.cs`, `WindowsProcessTiming.cs`

A new seam matching the existing `IClickEngine` / `IHotkeyWatcher` pattern.

```csharp
public interface IProcessTiming
{
    /// <summary>Asks the OS not to throttle this thread. Call from the click thread.</summary>
    void KeepResponsive();

    /// <summary>What the OS is actually granting, for the timing readout.</summary>
    string Describe();
}
```

`MacProcessTiming` calls
`pthread_set_qos_class_self_np(QOS_CLASS_USER_INTERACTIVE, 0)` via a
`libSystem` P/Invoke. Plain C, no Objective-C runtime, nothing that can
silently half-work.

`WindowsProcessTiming` ports the real `ProcessTiming.cs`: the EcoQoS opt-out
via `SetProcessInformation` with `ProcessPowerThrottlingInformation`,
`ControlMask = IgnoreTimerResolution | ExecutionSpeed` and `StateMask = 0`,
plus `GetProcessInformation` and `NtQueryTimerResolution` read-backs for
`Describe()`.

That Windows implementation is deliberate, not scope creep.
`Engine/IClickEngine.cs` states the principle: a working Windows
implementation sits beside every Mac one so the whole app can be run and judged
on a PC. A no-op stub would break it, leaving no way to tell the seam works.

### Changed files

| File | Change |
| --- | --- |
| `Engine/IClickEngine.cs` | `MouseDown(ClickButton)` / `MouseUp(ClickButton)` |
| `Engine/MacClickEngine.cs` | button-aware `Post`; CGEvent pairing table |
| `Engine/WindowsClickEngine.cs` | same, via the Win32 flag table |
| `Core/Clicker.cs` | `Button` in `ClickSettings`; diagnostics; GC mode; QoS |
| `Core/ClickTiming.cs` | floors via `RemoteConfig`; delete the disproved model |
| `Core/Presets.cs` | add `Measured` first |
| `Core/AppSettings.cs` | persist `ClickButton` and wallpaper name |
| `MainWindow.axaml` | button selector, verdict line, timing expander, picker |
| `MainWindow.axaml.cs` | wire the above; load config at startup |
| `Core/Updater.cs` | `Owner`/`Repo` consts; version to 1.1.0 |
| `Testing/JinxyMac.Tests.csproj` | add each new file and test file by hand |

#### `MacClickEngine` button table

`MacClickEngine` currently hardcodes `EventLeftMouseDown = 1`,
`EventLeftMouseUp = 2` and `MouseButtonLeft = 0`. It gains a table:

| Button | Down type | Up type | Button number |
| --- | --- | --- | --- |
| Left | 1 | 2 | 0 |
| Right | 3 | 4 | 1 |
| Middle | 25 | 26 | 2 |

(`kCGEventLeftMouseDown/Up`, `kCGEventRightMouseDown/Up`,
`kCGEventOtherMouseDown/Up`; `kCGMouseButtonLeft/Right/Center`. confidence:
high)

Everything else about `Post` is unchanged and must stay that way — in
particular `CGEventSetIntegerValueField(click, EventFieldClickState, 1)` and
the pressure pairing, which are what made the clicks land in Roblox at all.

#### `Core/ClickTiming.cs`

`HitFixMinDownMs` and `HitFixMinUpMs` become properties reading
`RemoteConfig.Current`, with `DefaultHitFixMinDownMs`/`DefaultHitFixMinUpMs`
kept as consts so the shipped, tested numbers survive as facts.

Separately, `Resolve`'s remarks still carry the **disproved** model:

> A client reads input once a frame. At 60 fps that is every ~17ms, so a press
> shorter than a frame can begin and end between two reads and never be seen.

Windows deleted that wording in `1b34a60`. The app's own measured profile has a
gap of a third of a frame and wins anyway; a competing clicker holds for 15.6ms
— under a frame — and its presses register. Roblox takes mouse events off the
message queue. The comment is replaced with the reasoning that survived: the
floors only ever need to keep press and gap from collapsing to nothing.

#### `Core/Presets.cs`

`new ClickPreset("Measured", 41.2, 77.37, holdMode: true)` goes first in
`Defaults()`, with the comment explaining that all three values were measured
together, frame by frame, off a match where this configuration landed 34 hits
against a 193 CPS setup's 33.

Note the limit: `PresetStore` deliberately persists the whole list so deleted
defaults stay deleted. Existing users therefore will not see `Measured` until
they use Restore. That is correct behaviour, not a bug to work around.

## The three details that carry the risk

### 1. QoS must be set from the click thread

`pthread_set_qos_class_self_np` sets the QoS of the **calling** thread. Called
from `Clicker.Start()` it would set the UI thread's QoS and do precisely
nothing for clicking — while returning success.

`KeepResponsive()` is therefore called from inside `Loop`, and re-asserted on
the same 30-second cadence the Windows loop uses, for the same reason: being
granted something at launch is not keeping it, and the system's judgement is
made long after startup.

The call returns non-zero on an invalid QoS class. That return is checked and
surfaced through `Describe()`, so a wrong constant shows up in the timing
readout instead of failing silently. (`QOS_CLASS_USER_INTERACTIVE` is believed
to be `0x21` — confidence: moderate. Verify against `sys/qos.h`; the return
check is what makes a wrong guess visible rather than invisible.)

Whether a pthread QoS override survives .NET's thread management is not certain
(confidence: moderate). The click thread is a dedicated `new Thread(...)`
rather than a threadpool thread, which is the case most likely to hold.

### 2. The release must match the press

`Clicker.Loop`'s `finally` currently calls `_engine.MouseUp()` with no
argument. Once buttons exist, a right-press paired with a left-release leaves
the **right button held down across the entire desktop** with nothing to
release it — worse on macOS than Windows, where it is a stuck context menu.

The loop tracks the button it actually pressed and releases that one, whatever
the selector says now. This is the same correction the Windows file documents:
"The one that was pressed, whatever is selected now."

The existing `lock (_inputGate)` around the down/up pair stays exactly as it
is. It is what stops shake splitting a click into a drag.

### 3. The headline number does not change source

`MainWindow.axaml.cs:481` computes `Measured {rate:0.0} /s` from a one-second
`ClickCount` delta. That stays, and that same value feeds
`ClickOutput.Classify`, matching what Windows shows the public.

`ClickDiagnostics` supplies the expander only. Swapping the visible number to a
512-gap median is a behaviour change nobody asked for, and the delta is what
users have been reading.

## Data flow

```
startup ──> RemoteConfig.LoadAsync ──> RemoteConfig.Current
                                            │
                                            ├─> ClickTiming.HitFixMin*Ms
                                            ├─> recorder availability
                                            └─> notice line

click loop ──> IProcessTiming.KeepResponsive()      (entry, then every 30s)
           ──> GCSettings.LatencyMode = SustainedLowLatency  (restored in finally)
           ──> IClickEngine.MouseDown(button)
           ──> IClickEngine.MouseUp(button) ──> ClickDiagnostics.RecordClick()

1s tick ──> ClickCount delta ──> MeasuredText
                             └─> ClickOutput.Classify ──> Verdict line
        ──> ClickDiagnostics.Current() ──> Timing detail expander
        ──> IProcessTiming.Describe()  ──> Timing detail expander
```

`ClickDiagnostics.Reset()` is called when the loop starts, so gaps carried over
from an earlier run — with an idle stretch in the middle — do not show as an
enormous stall that never happened.

## Error handling

Unchanged in character from both existing codebases: nothing in this feature
set may take the app down.

- `RemoteConfig` swallows everything and falls back to shipped defaults.
- `MacProcessTiming` catches and reports through `Describe()` rather than
  throwing; a machine where the call fails clicks exactly as it does today.
- `GCSettings.LatencyMode` is refused under some hosting configurations; caught
  and ignored, and restored in `finally` so the process is not left in a
  low-latency mode after clicking stops.
- `Wallpaper` copy failures leave the previous background in place.
- `ClickDiagnostics.Gaps()` already discards non-positive gaps, for machines
  whose clock goes backwards.

## Testing

`Testing/JinxyMac.Tests.csproj` sets `EnableDefaultCompileItems=false` and
lists every file explicitly, so each addition is a manual csproj edit.

New suites, all ported from Windows, none requiring a Mac:

| Suite | Covers |
| --- | --- |
| `ClickDiagnostics.Tests` | ring wrap, gap maths, median vs mean, stall count, clock going backwards |
| `ClickOutput.Tests` | all five `OutputState` cases, NaN refusal, the tolerance band |
| `RemoteConfig.Tests` | clamping, unknown keys, wrong types, bad JSON, notice truncation, host pinning |
| `ClickButton.Tests` | round-trip parse, unknown name falls to Left, label text |
| `Wallpaper.Tests` | supported extensions, stored name, missing source |

Dependencies pulled in but not tested directly: `SettingsPath`, and
`ClickTiming` gains a `RemoteConfig` dependency the test project must now
compile.

## Deferred

**App Nap.** Opting out needs `beginActivityWithOptions:reason:` through
`objc_msgSend` — selector registration, the arm64 varargs ABI, and a returned
activity token that must be held for process lifetime or the opt-out silently
reverts. That is the app's worst failure shape repeated: it looks like it
worked, and the clicking just degrades. It is also the largest piece of
unverifiable-from-Windows code the app would contain. Revisit once a real Mac
can confirm the QoS work landed.

## Version

`Core/Updater.cs` `Version` goes `1.0.8` → `1.1.0`. It is the single place the
number is written; `Packaging/build-mac.sh` stamps it into `Info.plist`.

(The brief for this work said the Mac was at 1.0.4; the constant says 1.0.8.)
