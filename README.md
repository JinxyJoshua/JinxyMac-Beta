# JinxyMac — Beta

The Mac build of [JinxyClicker](https://github.com/JinxyJoshua/JinxyClicker).

Apple silicon and Intel, macOS 13 or later. Self-contained — nothing to install
for clicking, hotkeys, presets, shake or history. Only the recorder needs
anything extra, and it says so.

> **Beta, and the word is meant.** This was written on a Windows PC by someone
> without a Mac. Everything above the platform seam is tested and runs; the
> files that talk to macOS directly have never been executed. See
> [What is not tested](#what-is-not-tested).

## What it does

| Page | |
|---|---|
| **Clicker** | CPS and duty cycle to two decimals, typed or dragged. HitFix, Ultra Accuracy, shaky tracking. Hold or toggle mode. |
| **Presets** | The eleven shipped presets plus your own, saved between sessions. |
| **Recorder** | Screen capture through avfoundation, hardware encoded. Instant replay, and clip upload that hands back a link. |
| **History** | Time spent clicking and clicks delivered, by day. |
| **Theme** | Dark and light, twelve accents or your own hex, window opacity. |
| **Settings** | Hotkeys, permissions, clip folder, Roblox cache cleaner, menu bar item. |

Three bindable hotkeys for clicking — clicker, clicker + shake, and a fixed
building rate — plus one each for recording and saving a replay. All watched
system-wide so they work inside a game.

## Building

```bash
dotnet build            # the app
cd Testing && dotnet test   # 107 tests
```

### Packaging for macOS

```bash
bash Packaging/build-mac.sh
```

Runs on Windows. .NET cross-publishes a real Mach-O apphost for either macOS
target and an `.app` bundle is only a folder with a plist in it, so no Mac is
needed to produce one. Output lands in `dist/` as `JinxyMac-mac.tar.gz`.

Two things the script cannot do from a PC:

- **Signing.** The bundle is unsigned, so Gatekeeper blocks the first launch
  until the quarantine flag is cleared. `dist/README.txt` walks the user
  through it. Removing the step needs an Apple Developer account and a Mac to
  notarise from.
- **A universal binary.** That needs Apple's `lipo`. Instead both builds go in
  the bundle side by side and a launcher script picks one with `uname -m`, so a
  single download still runs anywhere.

It ships as `.tar.gz` rather than `.zip` deliberately: a zip written on Windows
carries no Unix permission bits, so every executable inside would arrive
without `+x` and the app would open and close with nothing to explain it.

## How it is put together

Everything platform-specific sits behind an interface, and there is a Windows
implementation of each one. That is the whole design: it means the app runs and
can be judged on a PC, and the untested surface is a handful of files rather
than the entire program.

```
Core/       Timing, presets, history, palette, cache cleaner — portable
Engine/     IClickEngine, IHotkeyWatcher + a Mac and a Windows implementation
Capture/    ffmpeg: screen recording, replay buffer, clip upload
Packaging/  Bundle assembly, Info.plist, icon generator
```

Tests sit next to the code they cover and are compiled by
`Testing/JinxyMac.Tests.csproj`, which links them in by source.

## What has been run on a Mac

Five files shipped in 1.0.0 having never been executed. Three of them now have.

| | State |
|---|---|
| `Engine/MacClickEngine.cs` | **Works.** Clicks register in Roblox as of 1.0.4. |
| `Engine/MacHotkeyWatcher.cs` | **Works.** Including Mouse 4 and Mouse 5 as bindings. |
| `Engine/MacPermissions.cs` | **Works.** Correctly reported both permissions missing. |
| `Core/SystemLoad.cs` | Never run. Raw Mach struct layouts. |
| `Capture/` avfoundation path | Never run against a real screen. |
| `Core/Updater.cs` | Never run. The bundle swap in particular. |

Two bugs were found in the click path by testing, and both were the kind
nothing local would have caught:

- Events carried no `kCGMouseEventClickState`. A mouse-down with click state
  zero is not part of a click sequence; AppKit forwards it to a button anyway,
  so it worked everywhere *except* an application reading events itself. A game
  is exactly that.
- Every synthetic event suppressed real input for 250ms — the default local
  events suppression interval on an event source. At 20 CPS a post lands every
  50ms, so the window never closed and macOS stopped seeing the keyboard,
  including the hotkey meant to stop the clicker.

Two more were found in `SystemLoad.cs` by reading rather than running: a short
buffer the kernel would have overrun, and a leaked Mach port right per tick.

Every Mac bug so far has been in these files. None has come from the tested
core, which is the seam earning its keep.

## Relationship to the Windows app

Separate application, not a port. It shares the timing engine, the presets and
the settings file format — copy `history.json` across and the numbers follow —
but nothing in the Windows repo was modified to make this exist.

The Tweaks and Optimizations pages did not come over. Registry keys, power
plans and network tuning have no macOS equivalent; the one part that did
transfer is the Roblox cache cleaner, on the Settings page.
