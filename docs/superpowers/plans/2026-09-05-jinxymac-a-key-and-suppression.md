# The A Key and Window Suppression Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the two limitations JinxyMac 1.2.0 shipped with — the A key being unbindable, and macros typing into this app's own window.

**Architecture:** Task 1 moves the "unbound" sentinel from `0` to `-1` behind a one-time settings migration, so `0` can mean the A key. Task 2 wires `MacroRunner.Suppressed` to the window's own activation state, which needs no platform interop at all.

**Tech Stack:** C#, .NET 10 (`net10.0`), Avalonia 11.3, xunit 2.9.2.

## Global Constraints

- **`Core/` must contain NO Avalonia dependency** — `Core/CorePurity.Tests.cs` enforces it and will fail the build.
- `net10.0`, no `-windows` suffix, never WPF.
- `Testing/JinxyMac.Tests.csproj` lists files by hand; append only.
- Build must stay at **0 warnings**. Baseline: **448 tests passing**.
- Nothing may crash the app. Every file read falls back to defaults.
- Commit messages end with: `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`

---

### Task 1: Move the unbound sentinel to −1, and migrate

**Why this is not a one-line change.** On macOS `kVK_ANSI_A` is `0`. So is the value every existing `settings.json` and `macros.json` uses to mean "no hotkey bound". Flipping the sentinel without a migration would read every unbound hotkey as **A** — silently binding a movement key for everyone who bound nothing.

A schema version is what separates the two meanings: below it, `0` means unbound; at or above it, `0` means A.

**Files:**
- Modify: `Core/AppSettings.cs`, `Core/HotkeyBinding.cs`, `Core/KeyMacro.cs`, `Core/KeyCodes.cs`
- Modify: `Engine/MacHotkeyWatcher.cs`, `Engine/MacKeyEngine.cs`
- Modify: `MainWindow.axaml.cs`, `MainWindow.Macros.cs`, `MainWindow.Switcher.cs`, `Core/SwitcherMacro.cs`
- Modify: `Testing/JinxyMac.Tests.csproj` if new test files are added

- [ ] **Step 1: Add the schema version and migrate `settings.json`**

`Core/AppSettings.cs` holds six fields that store a key code: `HotkeyCode`, `ComboCode`, `BuildCode`, `RecordCode`, `ReplayCode`, `SwitcherHotkeyCode`. All are plain `int` defaulting to `0`.

Add `public int SchemaVersion { get; set; }` and a `const int CurrentSchema = 1`. In `Load()`, after deserialising, if the loaded `SchemaVersion` is below `CurrentSchema`:

- rewrite every one of those six fields from `0` to `-1`
- set `SchemaVersion = CurrentSchema`
- save immediately, so the migration happens once rather than on every launch

Change each field's default to `-1`.

Document it properly: a comment saying `0` used to mean unbound and now means the A key, that the version is what tells the two apart, and that migrating on read rather than guessing from the name is what stops someone's unbound hotkey becoming a movement key.

- [ ] **Step 2: Migrate `macros.json`**

`MacroStore`'s `StoredFile` already carries a `Platform` field. Add a version alongside it, migrated the same way: below the current version, a per-macro `HotkeyCode` of `0` becomes `-1`.

`Load()` currently reads `hotkey: m.HotkeyCode > 0 ? new HotkeyBinding(...) : HotkeyBinding.Unbound` (around line 683) — that `> 0` becomes `>= 0` **after** migration has run.

- [ ] **Step 3: Move the sentinel**

`Core/HotkeyBinding.cs`:
```csharp
    public static readonly HotkeyBinding Unbound = new(-1, "Not set");

    public bool IsValid => Code >= 0;
```

Replace the doc comment explaining why A is unbindable — that limitation is gone. Replace it with why the sentinel is `-1`: because `0` is a real key on macOS, and a sentinel has to be a value no key can take.

- [ ] **Step 4: Let 0 through every filter**

| File | Change |
| --- | --- |
| `Core/KeyMacro.cs` | `Keys = keys.Where(k => k is > 0 and < 256)` → `>= 0` |
| `Engine/MacHotkeyWatcher.cs:63` | `code is > 0 and < 128` → `>= 0` |
| `Engine/MacKeyEngine.cs:51` | `if (code <= 0 ...)` → `if (code < 0 ...)`, and correct the comment above it |

Leave `Engine/WindowsHotkeyWatcher.cs`'s `c is > 0 and < 256` **alone** — virtual-key code 0 is not a key on Windows, and `KeyCodes.For` never produces it there. Add a one-line comment saying the two platforms differ deliberately and why.

- [ ] **Step 5: Teach `KeyCodes` the A key**

`Core/KeyCodes.cs`'s `Mac` switch deliberately omits `'A'`. Add `'A' => 0,` in its correct alphabetical place and delete the remark explaining the omission. Cross-check against `Engine/MacHotkeyWatcher.cs`'s `Name(int)` switch, which maps `0 => "A"`.

- [ ] **Step 6: Remove every "A cannot be bound" refusal**

Search for `UnbindableAMessage` and remove it and all its call sites — `MainWindow.axaml.cs`'s `Bind()`, `MainWindow.Macros.cs`'s `BindMacroHotkey` and `SaveMacro`, and `Core/SwitcherMacro.cs`'s `Build`. Remove the matching tests that assert A is refused, and **replace them with tests asserting A now works**: `KeyCodes.For('A')` is `0` on macOS, a macro can be built with A as a key, and a hotkey can be bound to A.

- [ ] **Step 7: Fix every `== 0` that meant "unbound"**

In `MainWindow.axaml.cs`: lines around 561 (`HotkeyCode == 0 ? ""`), 848 (`if (code == 0) return;` in `Fire`), 880, 921, and 3021 (`.Where(b => b.Code != 0)`). Each becomes a `< 0` / `>= 0` test.

**`Fire()`'s guard at ~848 is the one that matters** — with `0` now a real key, `if (code == 0) return;` would swallow every A press. Get it right and say in your report how you verified it.

- [ ] **Step 8: Write the migration tests**

These are the point of the task. Cover:
- a settings file with no `SchemaVersion` and `HotkeyCode: 0` loads as **unbound**, not as A
- after migration the file on disk holds `-1` and the current version
- a settings file already at the current version with `HotkeyCode: 0` loads as **A**
- the same three for `macros.json`
- migration runs once — loading twice does not double-migrate or lose a real binding

Use the existing isolation pattern in `Core/KeyMacro.Tests.cs` (back up the real file, restore in a `finally`).

- [ ] **Step 9: Build, run the full suite, launch the app, commit**

---

### Task 2: Stop macros typing into this window

**Files:**
- Modify: `MainWindow.axaml.cs`, `Core/KeyMacro.cs` (doc comment only), `MainWindow.Switcher.cs`, `MainWindow.Macros.cs`

- [ ] **Step 1: Track whether this window is in front**

`MacroRunner.Suppressed` is a `Func<bool>?` read from the macro thread (`Core/KeyMacro.cs`, in `Loop`). It is declared but assigned nowhere.

**No platform interop is needed.** Avalonia's `Window.IsActive` already means "this window has focus", on both platforms.

But `IsActive` is a UI property and the macro thread must not read it. So cache it:

- add a `private volatile bool _windowActive;`
- subscribe to the window's activation changes on the UI thread and update the field
- set `Suppressed = () => _windowActive` where the runner is constructed

Use whichever Avalonia mechanism is correct for observing activation — `Activated`/`Deactivated` events, or `IsActiveProperty` change notifications. Check which actually fires reliably and say which you used and why.

- [ ] **Step 2: Correct the doc comment on `Suppressed`**

It currently says the property is unassigned in this build. It is assigned now — say what it is wired to and what that means: the cycle still advances while suppressed, so alt-tabbing away and back does not leave a macro stuck on one key.

- [ ] **Step 3: Update both pages' wording**

`MainWindow.Switcher.cs` and `MainWindow.Macros.cs` both carry warnings written when nothing was suppressed. They should now say that keys are held back while this window is in front, so switching to the game is what starts them landing.

**Do not overstate it.** A macro still sends to whatever window is focused once this one is not — that has not changed. The guarantee is only that it will not type into JinxyMac itself.

- [ ] **Step 4: Verify it actually suppresses**

Add a test against `MacroRunner` with a fake key engine and a `Suppressed` that returns true: the loop must send nothing while suppressed, and must resume when it returns false. `Core/MacroRunner.Tests.cs` already has the fake-engine pattern — follow it.

- [ ] **Step 5: Build, run the full suite, launch the app, commit**

---

### Task 3: Version and verify

- [ ] **Step 1:** `Core/Updater.cs` `Version` → `1.2.1`.
- [ ] **Step 2:** Full suite, 0 warnings.
- [ ] **Step 3:** Release publish both architectures; `grep -a -c 'rschi'` must be `0` in each.
- [ ] **Step 4:** Commit.
