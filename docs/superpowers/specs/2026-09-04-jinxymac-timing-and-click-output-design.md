# JinxyMac 1.1.0 — click output, remote config, wallpaper, build hygiene

Date: 2026-09-04
Status: design, awaiting review
Supersedes: the first revision of this file, which also scoped `ClickDiagnostics`
and a macOS background-throttle defence. Both were cut on instruction — see
"Cut on instruction" below, which records what was removed and why it mattered.

## Why this exists

JinxyClicker (Windows) moved 18 commits between 2026-08-23 and 2026-09-02.
JinxyMac has none of it. This spec covers the first of several ports.

### Correcting the framing this started from

The port was described as a list of small fixes that "port cleanly": the
per-click allocation fix, the CPS advice correction, kit search, per-hotkey
unbind, the kit-art alpha bug, the first-run kit list.

Most of that list cannot be ported, because it is fixes to subsystems JinxyMac
does not have:

| Item | Lives in | Mac status |
| --- | --- | --- |
| kit search | `MainWindow.KitWheel.cs:203` | no kit wheel |
| kit-art alpha bug, first-run kit list | `KitArt*.cs` | no kit art |
| per-hotkey unbind | `MainWindow.xaml.cs:4649` | no macro system |
| CPS advice correction | `ClickOutput.cs:78` | no `ClickOutput.cs` |
| per-click allocation fix | `ClickOutput`/`ClickTiming` in `1b34a60` | **no target — see below** |

Windows has 14 nav pages; the Mac has 6. There is no Macros page, no Kit Wheel
page, no Switcher page. Those are features to build, not bugs to fix.

**The per-click allocation fix has nothing to fix here.** The Windows bug was
`SendMouseEvent` constructing a one-element `INPUT[]` array per mouse event —
two heap allocations per click, measured at 14.6 MB/hour. `MacClickEngine.Post`
passes a `CursorPoint` **struct** and allocates nothing on the managed heap; the
`CGEvent` objects it creates are native and `CFRelease`d, so the GC is never
involved. There is no equivalent defect to port.

## Scope

**In:**

- **Click output.** `ClickOutput`, `ClickButton` (right / middle / wheel), the
  `Measured` preset, and the corrected CPS advice.
- **Remote config.** `RemoteConfig` + `config.json`, with `ClickTiming`'s HitFix
  floors reading through it.
- **Wallpaper.** `Wallpaper.cs` and a picker on the Theme page.
- **Build hygiene.** `PathMap`, `Deterministic`, and no debug record in Release.

**Out, each needing its own spec:** macros, kit wheel and kit art, update-flow
changes, telemetry, dev tools, `NavIcon.cs`, streamer mode.

## Cut on instruction

Recorded so the reasoning is not lost, and so a later spec can pick it up.

### `ClickDiagnostics.cs`

Cut as a dev tool. Worth noting for whoever revisits: on Windows
`ClickDiagnostics.RecordClick()` is called from the **public** click loop at
`MainWindow.xaml.cs:2208`, and only its *display* lives in
`MainWindow.DevTools.cs` — the file itself is public-build code.

Consequence: JinxyMac has no way to measure its own timing. There is no dev
build on macOS, and the developer has no Mac. A user reporting degradation can
supply only a description.

### The macOS background-throttle defence

Cut. `ProcessTiming.cs` was assessed as Win32-only, needing "a macOS equivalent
or nothing". That assessment is wrong, and the finding is recorded here because
it will otherwise be re-derived:

- **App Nap** applies CPU priority lowering, I/O priority lowering, and timer
  throttling to apps the system judges inactive. JinxyMac's window is always
  occluded in real use — the game is in front. This is the `timeBeginPeriod`
  half. (confidence: high)
- **Thread QoS.** On Apple Silicon, threads at background QoS are confined to
  efficiency cores. This is EcoQoS near-exactly: applied over a session rather
  than at launch — the precise shape of "fine for two hours, then the hit
  registration gets worse". (confidence: high)

`Clicker.Start()` sets `ThreadPriority.AboveNormal`, which .NET maps onto POSIX
nice values; that does not prevent QoS-based efficiency-core confinement.
(confidence: moderate)

Consequence: the degradation the Windows app has now fixed twice remains
unaddressed on macOS.

Also dropped with it: `GCSettings.LatencyMode = SustainedLowLatency` in the
click loop. That one is pure .NET, one line, and unrelated to `ProcessTiming` —
it is a one-line addition if wanted later.

## The five duplicated files are not shared

Sharing was the stated lean. It is rejected — only two of the five are
shareable at all.

| File | Win LOC | Mac LOC | Diff lines | Verdict |
| --- | --- | --- | --- | --- |
| `ClickTiming` | 129 | 106 | 35 | shareable in principle |
| `ClickHistory` | 155 | 140 | 70 | shareable in principle |
| `AppSettings` | 176 | 111 | 289 | diverged, legitimately |
| `Updater` | 163 | 286 | 451 | diverged — Mac's is **bigger** |
| `Presets` | — | 170 | n/a | does not exist on Windows |

`Presets.cs` has no Windows counterpart; Windows has `ClickPreset.cs`, a
different 206-line file. The Mac `Updater` is larger because it performs
detached bundle-swapping, which Windows has no analogue for — it has diverged,
not drifted.

The mechanism is also worse than it appears. JinxyMac-Beta is a separate public
repository that must clone and build standalone. A
`<Compile Include="../../MyBlinkStyleClicker/…" />` link resolves only because
the two repos happen to be siblings on one developer's disk — a property of that
machine, not of the projects. Anyone cloning JinxyMac-Beta alone gets a build
error. A NuGet package or submodule fixes that but adds a feed, versioning
ceremony, and a release step in order to change one constant, for 235 lines.

**Decision:** port file-by-file. If drift protection is wanted later, a test
that diffs the two files modulo namespace is far cheaper than coupling builds.

## Architecture

### New files

| File | Port fidelity |
| --- | --- |
| `Core/ClickOutput.cs` | **Verbatim**, namespace only — pure arithmetic and strings |
| `Core/ClickButton.cs` | **Split** — the enum and `Label`/`Parse` come across; the Win32 flag table does not |
| `Core/RemoteConfig.cs` | Trimmed to four fields |
| `Core/Wallpaper.cs` | Port; the WPF filter string becomes an Avalonia `FilePickerFileType` |

#### `Core/ClickOutput.cs`

Carries the corrected advice: `DiminishingReturnsCps = 36.0` (down from 50),
`MeasuredBestCps = 33.3`, `MismatchTolerance = 0.15`.

The doc comment explaining why 8–12 CPS community advice was wrong — it
described Minecraft's Bedwars, a different game on a different server — comes
across intact. It is load-bearing: the mistake is easy to repeat.

#### `Core/ClickButton.cs`

The `ClickButton` enum (`Left`, `Right`, `Middle`) and the `Label`/`Parse`
helpers are portable. `Label` keeps the Windows wording, where `Middle` displays
as "Wheel". The `ClickButtons` Win32 flag table does not come across; platform
equivalents live in the engine implementations.

`Parse` keeps its "anything unrecognised is Left" behaviour: a hand-edited
settings file naming a button that does not exist should leave a working
clicker, not one that presses nothing.

#### `Core/RemoteConfig.cs`

| Key | Type | Bounds | Default |
| --- | --- | --- | --- |
| `hitFixMinDownMs` | double | 1–100 | 15.0 |
| `hitFixMinUpMs` | double | 1–100 | 15.0 |
| `recorderEnabled` | bool | — | true |
| `notice` | string | ≤200 chars | "" |

`macroEquipMs` and `kitArtFetchEnabled` are dropped — the Mac has neither
feature, and dead keys in a file whose entire value is being trustworthy are a
small lie.

The invariants are carried across unchanged and are not negotiable:

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

The case is stronger on Mac than on Windows: a release costs Mac users real
pain, since the app is unsigned, so replacing the bundle makes it a new app to
Accessibility and the grant must be removed and re-added by hand. Correcting a
timing floor by editing one file in a browser skips all of that.
`recorderEnabled` matters more here too — ffmpeg and Screen Recording have
already produced two real bugs on hardware that cannot be tested.

`Updater` currently holds a private `Feed` const at `Core/Updater.cs:40`. This
adds `public const string Owner = "JinxyJoshua"` and
`public const string Repo = "JinxyMac-Beta"`, rebuilds `Feed` from them, and has
`RemoteConfig.Url` derive from the same pair, so the repository is named once. A
`config.json` is committed at the root of JinxyMac-Beta.

#### `Core/Wallpaper.cs`

The logic — copy the chosen file into the settings folder, store a bare name
rather than a path — is fully portable and has no Win32 in it. Two changes:

- `FileFilter`, a WPF filter string, is replaced by an Avalonia
  `FilePickerFileType` for `IStorageProvider.OpenFilePickerAsync`.
- The `Allowed` list keeps `.png .jpg .jpeg .bmp .webp`. Avalonia decodes
  through SkiaSharp, which handles all five. (confidence: moderate — verify
  `.webp` decodes; if it does not, drop it from the list rather than shipping a
  format that fails at load.)

### Changed files

| File | Change |
| --- | --- |
| `Engine/IClickEngine.cs` | `MouseDown(ClickButton)` / `MouseUp(ClickButton)` |
| `Engine/MacClickEngine.cs` | button-aware `Post`; CGEvent pairing table |
| `Engine/WindowsClickEngine.cs` | same, via the Win32 flag table |
| `Core/Clicker.cs` | `Button` in `ClickSettings`; track the pressed button |
| `Core/ClickTiming.cs` | floors via `RemoteConfig`; delete the disproved model |
| `Core/Presets.cs` | add `Measured` first |
| `Core/AppSettings.cs` | persist `ClickButton` and wallpaper name |
| `MainWindow.axaml` | button selector, verdict line, wallpaper picker |
| `MainWindow.axaml.cs` | wire the above; load config at startup |
| `Core/Updater.cs` | `Owner`/`Repo` consts; version to 1.1.0 |
| `JinxyMac.csproj` | `PathMap`, `Deterministic`, Release debug record off |
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
`kCGEventOtherMouseDown/Up`; `kCGMouseButtonLeft/Right/Center`.
confidence: high)

Everything else about `Post` is unchanged and must stay that way — in particular
`CGEventSetIntegerValueField(click, EventFieldClickState, 1)` and the pressure
pairing, which are what made the clicks land in Roblox at all.

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
message queue. Replaced with the reasoning that survived: the floors only ever
need to keep press and gap from collapsing to nothing.

#### `Core/Presets.cs`

`new ClickPreset("Measured", 41.2, 77.37, holdMode: true)` goes first in
`Defaults()`, with the comment explaining that all three values were measured
together, frame by frame, off a match where this configuration landed 34 hits
against a 193 CPS setup's 33.

Note the limit: `PresetStore` deliberately persists the whole list so deleted
defaults stay deleted. Existing users will not see `Measured` until they use
Restore. That is correct behaviour, not a bug to work around.

## Build hygiene — a live privacy leak

`JinxyMac.csproj` has no `PathMap`, no `Deterministic`, and no Release debug
setting. The shipped 1.0.8 bundle therefore carries the build path in both
architecture binaries:

```
JinxyMac.app/Contents/MacOS/arm64/JinxyMac.dll
JinxyMac.app/Contents/MacOS/x64/JinxyMac.dll
  → C:\Users\rschi\_dev\Joshua\JinxyMac\obj\Release\net10.0\osx-arm64\JinxyMac.pdb
```

One occurrence in each, readable in any hex editor. That is the developer's
Windows account name and full source path, in a public download with 21
recorded downloads at time of writing.

Windows fixed this in `d821eb6` on 2026-08-28. The same block ports verbatim:

```xml
<PathMap>$(MSBuildProjectDirectory)=.</PathMap>
<Deterministic>true</Deterministic>
```

plus, for Release only, `<DebugType>none</DebugType>` and
`<DebugSymbols>false</DebugSymbols>` — the `.pdb` reference is itself an
absolute path to a file the user does not have, leaking the same two names to no
benefit since symbols are not shipped. Debug builds keep theirs so local
debugging still works.

This is the cheapest item in the spec and the only one already affecting users.

## The two details that carry the risk

### 1. The release must match the press

`Clicker.Loop`'s `finally` currently calls `_engine.MouseUp()` with no argument.
Once buttons exist, a right-press paired with a left-release leaves the **right
button held down across the entire desktop** with nothing to release it — worse
on macOS than Windows, where it is a stuck context menu.

The loop tracks the button it actually pressed and releases that one, whatever
the selector says now. This is the same correction the Windows file documents:
"The one that was pressed, whatever is selected now."

The existing `lock (_inputGate)` around the down/up pair stays exactly as it is.
It is what stops shake splitting a click into a drag.

### 2. The headline number keeps its source

`MainWindow.axaml.cs:481` computes `Measured {rate:0.0} /s` from a one-second
`ClickCount` delta. That stays, and that same value feeds
`ClickOutput.Classify`, matching what Windows shows the public. With
`ClickDiagnostics` cut, this delta is now the app's **only** measure of
delivered rate, so it must not be disturbed.

## Data flow

```
startup ──> RemoteConfig.LoadAsync ──> RemoteConfig.Current
                                            ├─> ClickTiming.HitFixMin*Ms
                                            ├─> recorder availability
                                            └─> notice line

click loop ──> IClickEngine.MouseDown(button)
           ──> IClickEngine.MouseUp(button)

1s tick ──> ClickCount delta ──> MeasuredText
                             └─> ClickOutput.Classify ──> Verdict line
```

## Error handling

Nothing in this feature set may take the app down.

- `RemoteConfig` swallows everything and falls back to shipped defaults.
- `Wallpaper` copy failures leave the previous background in place.
- A failed button post must never bring the click loop down, as today.

## Testing

`Testing/JinxyMac.Tests.csproj` sets `EnableDefaultCompileItems=false` and lists
every file explicitly, so each addition is a manual csproj edit.

| Suite | Covers |
| --- | --- |
| `ClickOutput.Tests` | All five `OutputState` cases, NaN refusal, the tolerance band |
| `RemoteConfig.Tests` | Clamping, unknown keys, wrong types, bad JSON, notice truncation, host pinning |
| `ClickButton.Tests` | Round-trip parse, unknown name falls to Left, label text |
| `Wallpaper.Tests` | Supported extensions, stored name, missing source |

All portable. None require a Mac. `ClickTiming` gains a `RemoteConfig`
dependency the test project must now compile.

## Version

`Core/Updater.cs` `Version` goes `1.0.8` → `1.1.0`. It is the single place the
number is written; `Packaging/build-mac.sh` stamps it into `Info.plist`.

(The brief for this work said the Mac was at 1.0.4; the constant says 1.0.8.)
