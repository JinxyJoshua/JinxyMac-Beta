# Macros, Auto Switcher and Kit Wheel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give JinxyMac the three pages it is missing — Macros, Auto Switcher, and Kit Wheel — bringing it level with JinxyClicker apart from the Win32-only features.

**Architecture:** The macro engine already exists and is tested (`MacroRunner`, `KeyMacro`, `MacroStore`, `IKeyEngine`). The switcher is not new code: it is a `KeyMacro` under the reserved name `" AutoSwitcher"`. The kit wheel's logic ports almost untouched — `KitWheel`, `KitImages`, `KitArt` and `KitArtFetch` have **zero** WPF references between them. Only `KitArtImage`'s decoder and the whole page layer need real work.

**Tech Stack:** C#, .NET 10 (`net10.0`), Avalonia 11.3, xunit 2.9.2.

**Source being ported:** `.superpowers/port-source/` — extracted from the Windows app. Read the file you need; do not read them all.

**Windows XAML for reference** (`C:\Users\rschi\_dev\Joshua\MyBlinkStyleClicker\MyBlinkStyleClicker\MainWindow.xaml`):
- `PageMacros` — lines 1473-1585
- `PageSwitcher` — lines 1586-1717
- `PageKitWheel` — lines 1718-2114

## Global Constraints

- Namespaces: `JinxyMac.Core` for logic, `JinxyMac.Engine` for platform seams. Tests in `JinxyMac.Core.Tests`.
- **`Core/` must contain NO Avalonia dependency.** The test project compiles Core files and has no Avalonia reference. Anything touching `Bitmap`, `FilePicker`, controls or brushes lives in the window layer.
- `net10.0`, no `-windows` suffix, never WPF.
- `Testing/JinxyMac.Tests.csproj` sets `EnableDefaultCompileItems=false`; append entries by hand, never disturbing existing ones.
- Build must stay at **0 warnings**. Baseline: **274 tests passing**.
- Nothing may crash the app. Every file, network and interop call is wrapped and falls back.
- New pages follow the existing page conventions in `MainWindow.axaml`: a `StackPanel` named `Page<Name>` with `IsVisible="False"`, a matching `RadioButton Classes="nav" GroupName="Nav" Name="Nav<Name>"`, cards as `Border Classes="card"`, labels as `Classes="label"`, hints as `Classes="hint"`. **Read an existing page before writing a new one and match it.**
- Commit messages end with: `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`

---

### Task 1: Kit wheel logic

The parts with no UI in them at all. `KitWheel.cs` (349 lines), `KitImages.cs` (175), `KitArt.cs` (210) — zero WPF references between them.

**Files:**
- Create: `Core/KitWheel.cs`, `Core/KitImages.cs`, `Core/KitArt.cs`
- Create: their four ported test files
- Modify: `Testing/JinxyMac.Tests.csproj`

**Interfaces:**
- Consumes: `SettingsPath.For(string)`
- Produces: the roster, roll-without-replacement, saved wheels, and the wiki naming/parsing helpers. Read the source for exact signatures and record them in your report so Task 3 can consume them.

- [ ] **Step 1: Port the three files**

Copy from `.superpowers/port-source/`, changing the namespace to `JinxyMac.Core`. Every doc comment comes across verbatim.

Check as you go for anything WPF-shaped that the grep missed — a `Dispatcher` reference, a control type in a signature. If you find one, stop and report it rather than inventing a substitute.

- [ ] **Step 2: Port the tests**

From `C:\Users\rschi\_dev\Joshua\MyBlinkStyleClicker\MyBlinkStyleClicker\`: `KitWheel.Tests.cs`, `KitImages.Tests.cs`, `KitArt.Tests.cs`. Namespace only. Do not weaken an assertion; if one fails, read why first and report it.

- [ ] **Step 3: Add all six files to the test project, build, run**

Expected: 274 plus the ported cases, all passing, 0 warnings.

- [ ] **Step 4: Commit**

Message: what the kit wheel is (a roster of 113 kits, rolled without replacement, with saved sets), and that the logic needed no conversion because it never touched the UI framework.

---

### Task 2: Kit art — decoding, fetching, and 8 MB of pictures

**Files:**
- Create: `Core/KitArtFetch.cs` (portable, 146 lines)
- Create: `MainWindow.KitArt.cs` or `Core/KitArtImage.cs` — see Step 2, the decision is yours to make and report
- Modify: `JinxyMac.csproj` (bundle `Assets/Kits/*.png`)
- Create: ported `KitArtImage.Tests.cs` where it still applies

**Interfaces:**
- Consumes: `KitArt` naming helpers from Task 1
- Produces: a way for the page to get a picture for a kit name

- [ ] **Step 1: Port `KitArtFetch.cs`**

Zero WPF references — it is `HttpClient` and file writes. Namespace only.

Keep its host pinning and its failure behaviour exactly: a fetch that fails leaves the kit without art rather than throwing.

- [ ] **Step 2: Deal with `KitArtImage.cs` (185 lines, 5 WPF references)**

Read it. Its job is decoding a downloaded file and framing it. The WPF references are its decode path — `BitmapImage`, `PreservePixelFormat`, `Bgra32`.

**Important context:** on Windows this file exists because the wiki serves WebP under `.png` names and WPF's default decode silently dropped the alpha channel, drawing every kit as a solid coloured rectangle. `PreservePixelFormat` plus an explicit `Bgra32` conversion was the fix.

Avalonia decodes through SkiaSharp, which handles WebP natively and does not have that bug. **Verify this before assuming it** — if Skia handles it, the decode simplifies to `new Bitmap(path)` and most of this file's complexity is unnecessary. Say plainly in your report which it turned out to be, and do not carry across a workaround for a bug that does not exist here.

Because `Bitmap` is an Avalonia type, whatever survives goes in the window layer, NOT in `Core/`.

- [ ] **Step 3: Bundle the pictures**

113 PNGs, 8.2 MB, already at `Assets/Kits/`. Windows ships them with `<Content Include="Assets\Kits\*.png" />`.

For an Avalonia `.app` bundle, add them as `AvaloniaResource` so they are embedded, or as `Content` copied to the output — pick one, and state in your report which you chose and why, including what it does to the bundle size. `Packaging/build-mac.sh` assembles the `.app` from the publish output; check that whichever you choose actually lands there.

- [ ] **Step 4: Verify a picture actually loads**

Write a test that resolves and decodes one real bundled kit image end to end. A test asserting only that a path string is built is not enough — the whole point of this task is that a picture appears.

- [ ] **Step 5: Build, run the suite, commit**

---

### Task 3: The Kit Wheel page

The largest piece: `MainWindow.KitWheel.cs` is 841 lines with 24 WPF references and needs a genuine Avalonia rewrite, not a port.

**Files:**
- Create: `MainWindow.KitWheel.cs` (Avalonia)
- Modify: `MainWindow.axaml` (the page and its nav entry)
- Modify: `MainWindow.axaml.cs` (wire it)

- [ ] **Step 1: Read both sides**

Read `.superpowers/port-source/MainWindow.KitWheel.cs` for the behaviour, and Windows `MainWindow.xaml` lines 1718-2114 for the layout. Then read an existing Mac page — `PagePresets` in `MainWindow.axaml` and its wiring — and match those conventions rather than transliterating WPF.

- [ ] **Step 2: Build the page**

From the screenshots of the Windows app, it carries: a KIT ROLL panel with a large result display reading `READY?` before the first roll, a `ROLL KIT` button, `CHALLENGE PROGRESS` with a `Start a new run` action, and `SAVED WHEELS` — name a set of kits and load it back by clicking it.

The reel animation is the part with no direct equivalent. Avalonia has its own animation system; use it rather than porting WPF `Storyboard` code. If a faithful reel proves impractical, a simpler cycling display that lands on the result is acceptable — say so in your report rather than shipping something half-animated.

- [ ] **Step 3: Wire selection, search and art**

Kit list with search, per-kit picture from Task 2, roll-without-replacement from Task 1.

- [ ] **Step 4: Build, run the suite, launch the app, commit**

Do NOT claim visual behaviour — the controller will screenshot it. Report what you could not verify.

---

### Task 4: The Macros page

The engine is done. This is the face for it.

**Files:**
- Create: `MainWindow.Macros.cs`
- Modify: `MainWindow.axaml`, `MainWindow.axaml.cs`

**Interfaces:**
- Consumes: `MacroRunner` (already constructed as `_macros`), `MacroStore`, `KeyMacro`, `KeyCodes.For`

- [ ] **Step 1: Read the Windows page**

`MainWindow.xaml` lines 1473-1585 for layout, and `grep -n "Macro" MainWindow.xaml.cs` in the Windows app for behaviour.

- [ ] **Step 2: Build the page**

From the screenshots it carries:
- A **HOTKEYS** card with a `Disable hotkeys` button and the warning that every bound key is live while editing.
- A **RUNNING** card listing what is active, with `Stop all`, and the note that a macro goes to whatever window is in front so the game must be switched to first.
- **Per-macro cards**: name, interval (`Every 10 ms`), a toggle hotkey, an enable switch, and `Enable`/`Delete` actions. A disabled macro shows a `DISABLED` overlay across the card.
- A **NEW MACRO** row: name, key, optional second key (fill it and the macro alternates), toggle hotkey, interval in ms, and `+ Save`.

- [ ] **Step 3: Parse keys through `KeyCodes`**

`MacroStore.ParseKeys` already translates typed keys into this platform's codes. Use it — do not re-implement parsing in the page.

Surface a clear message when a key cannot be bound. Note that **A cannot be bound on macOS**: its code is 0, which is also the "unbound" sentinel every settings file uses. `Core/HotkeyBinding.cs` documents why. The page must say so plainly rather than silently rejecting the input.

- [ ] **Step 4: Wire the toggle hotkeys**

Each macro can have its own hotkey. Route through the existing `IHotkeyWatcher` the way the clicker's hotkeys already are — read `WireHotkey()` in `MainWindow.axaml.cs` and follow it.

- [ ] **Step 5: Build, run the suite, launch, commit**

---

### Task 5: The Auto Switcher page

**Files:**
- Create: `MainWindow.Switcher.cs`
- Modify: `MainWindow.axaml`, `MainWindow.axaml.cs`, `Core/AppSettings.cs`

- [ ] **Step 1: Understand what it is**

The switcher is a `KeyMacro` named `" AutoSwitcher"` — the leading space keeps it out of the user's own macro names. It alternates two hotbar slots with asymmetric holds.

Windows persists its numbers in `AppSettings`, NOT in `macros.json`: `SwitcherSlotA`, `SwitcherSlotB`, `SwitcherIntervalMs`, `SwitcherIntervalBMs`, `SwitcherEquipMs`, `SwitcherDisabled`. Add the same fields here with the same defaults (`"3"`, `"1"`, 150, 900, 60).

**This matters:** `MacroStore` does not persist `HoldsMs`, `ClicksWanted` or `EquipMs`, so a switcher stored as a macro would lose its holds on restart. Rebuilding it from `AppSettings` each time is what avoids that. Do not try to store it in `macros.json`.

- [ ] **Step 2: Build the page**

From the screenshot: an **AUTO SWITCHER** card explaining it swaps between two hotbar slots on a timer, with five fields — `FIRST SLOT`, `SECOND SLOT`, `HOLD FIRST (MS)`, `EQUIP DELAY (MS)`, `HOLD SECOND (MS)` — a `Disable` button and a toggle, plus a status line. And a **KEYBIND** card turning it on and off from inside the game.

Carry across the reassurance verbatim, because it is the honest description of what the feature does: *"Slot keys are just the number keys Roblox already uses — the switcher presses 1 and 2 exactly as you would. It does not read the screen or the game."*

- [ ] **Step 3: Clear it on every stop path**

A note from the Windows history worth heeding: the switcher is a latch on its own thread, and every stop that was not its own hotkey used to leave it running — it kept swapping weapons with nothing clicking. It must be cleared in the one place all stop paths funnel through. Find that place in this app and put it there.

- [ ] **Step 4: Build, run the suite, launch, commit**

---

### Task 6: Version and final verification

- [ ] **Step 1:** `Core/Updater.cs` `Version` → `1.2.0`.
- [ ] **Step 2:** Full suite, 0 warnings.
- [ ] **Step 3:** Release publish both architectures; `grep -a -c 'rschi'` must be 0 in each — the build-path fix must still hold.
- [ ] **Step 4:** Report the `.app` bundle size, since 8 MB of kit art now ships in it.
- [ ] **Step 5:** Commit.

## Not in this plan

**Streamer mode / the macro badge.** `MacroBadge.cs` states on its own line 3 that `SetWindowDisplayAffinity` has no macOS equivalent, and it is WPF throughout. The badge itself could be rebuilt as an Avalonia overlay, but the half that hides it from OBS and Discord cannot be done on macOS. It needs its own spec and an honest decision about shipping a badge that cannot be hidden.
