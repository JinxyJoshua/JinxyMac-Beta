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

## What is not tested

Five files have never run:

| | Why it matters |
|---|---|
| `Engine/MacClickEngine.cs` | CGEvent. macOS discards synthetic input **silently** without Accessibility permission. |
| `Engine/MacHotkeyWatcher.cs` | `CGEventSourceKeyState` and the key code table. |
| `Engine/MacPermissions.cs` | The permission checks themselves. |
| `Core/SystemLoad.cs` | Raw Mach struct layouts. Two real bugs were found here by reading — a short buffer the kernel would overrun, and a leaked port right per tick. |
| `Capture/` avfoundation path | Argument shape is tested; capture against a real screen is not. |

The recorder retries without `-framerate` if a screen rejects the requested
rate, and keeps ffmpeg's stderr so a failure says *why* rather than that there
was one. Both exist because the failure could not be reproduced before release.

## Relationship to the Windows app

Separate application, not a port. It shares the timing engine, the presets and
the settings file format — copy `history.json` across and the numbers follow —
but nothing in the Windows repo was modified to make this exist.

The Tweaks and Optimizations pages did not come over. Registry keys, power
plans and network tuning have no macOS equivalent; the one part that did
transfer is the Roblox cache cleaner, on the Settings page.
