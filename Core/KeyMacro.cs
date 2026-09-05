using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using JinxyMac.Engine;

namespace JinxyMac.Core;

/// <summary>
/// A key, or a short cycle of keys, sent over and over on a timer.
/// </summary>
/// <remarks>
/// One shape covers both features people ask for. A macro that spams a single
/// key is the macro creator; a macro that alternates two keys is the inventory
/// switcher — sword to crossbow, pickaxe to gumdrop. Building the general one
/// gets the specific one for nothing, and the alternative was two engines that
/// drift apart.
/// </remarks>
public sealed class KeyMacro
{
    /// <summary>Fastest a macro may repeat. Below this it is a key that never comes up.</summary>
    public const int MinIntervalMs = 5;

    /// <summary>Slowest worth offering — beyond this a hotkey is easier.</summary>
    public const int MaxIntervalMs = 10_000;

    public KeyMacro(string name, IEnumerable<int> keys, string keysText, int intervalMs,
                    int[]? holdsMs = null, int clicksWanted = 0, int equipMs = DefaultEquipMs,
                    HotkeyBinding? hotkey = null, bool enabled = true)
    {
        Enabled = enabled;
        Name = name;
        // These are the platform's own key codes — CGKeyCodes here, virtual-key
        // codes on Windows. Both spaces pass this same 0 < k < 256 filter while
        // meaning entirely different keys; nothing this narrow can tell them
        // apart, which is why MacroStore's file format has to.
        //
        // The exclusion of 0 is load-bearing on macOS specifically: 0 is both
        // the A key and HotkeyBinding.Unbound's sentinel for "no key" — see
        // its remarks for why that collision isn't being fixed here.
        Keys = keys.Where(k => k is > 0 and < 256).ToArray();
        KeysText = keysText;
        IntervalMs = Math.Clamp(intervalMs, MinIntervalMs, MaxIntervalMs);

        HoldsMs = holdsMs?.Select(h => Math.Clamp(h, MinIntervalMs, MaxIntervalMs)).ToArray();

        ClicksWanted = Math.Max(0, clicksWanted);
        EquipMs = Math.Clamp(equipMs, 0, MaxIntervalMs);

        Hotkey = hotkey ?? HotkeyBinding.Unbound;
    }

    /// <summary>
    /// Whether this macro may run at all.
    /// </summary>
    /// <remarks>
    /// Switched off one macro at a time, rather than only by the master switch.
    /// Somebody with six macros bound usually wants five of them live and one
    /// out of the way — turning the lot off to silence a single key means
    /// remembering which were on before, and turning them back on one by one.
    ///
    /// Separate from being stopped: a disabled macro cannot be started by its
    /// key, by its switch, or by anything else, and stays that way across
    /// restarts.
    /// </remarks>
    public bool Enabled { get; }

    /// <summary>
    /// Toggles this macro on and off when pressed. Unbound means no shortcut —
    /// the switch on the card is the only way to start it.
    /// </summary>
    /// <remarks>
    /// The same type the global hotkeys use, so the polling loop, rebind flow,
    /// and collision check all treat a macro's toggle as one more binding
    /// rather than a parallel thing they had to learn about.
    /// </remarks>
    public HotkeyBinding Hotkey { get; }

    /// <summary>
    /// Clicks the first key must actually receive before the cycle moves on.
    /// </summary>
    /// <remarks>
    /// The point this feature turns on. A dwell measured in milliseconds is a
    /// guess about someone else's frame rate, equip animation and click rate,
    /// and it is wrong in both directions at once — too short and the weapon
    /// never fires, too long and it sits in your hand while the sword should
    /// be swinging.
    ///
    /// Counting the clicks the clicker has actually delivered replaces the
    /// guess with the thing the guess was approximating. The dip lasts exactly
    /// as long as it takes to fire and not one tick longer, whatever the CPS
    /// happens to be.
    ///
    /// Zero means time only, which is what an ordinary macro wants.
    /// </remarks>
    public int ClicksWanted { get; }

    /// <summary>Time to allow for the weapon to appear before clicks count.</summary>
    public int EquipMs { get; }

    /// <summary>
    /// How long to stay on each key, when they should not be equal.
    /// </summary>
    /// <remarks>
    /// The switcher needs this and an even rotation cannot give it. Half a
    /// second on the crossbow and half on the sword means the crossbow is in
    /// hand half the time and re-drawn on every return — which is why rotating
    /// fires slower than switching once by hand.
    ///
    /// Measurement turned this around. A recording of it done by hand shows the
    /// sword held for one to two seconds and the crossbow dipped to for about a
    /// sixth of one — the opposite of the guess this was built on. Null means
    /// every key gets IntervalMs, which is what an ordinary macro wants.
    /// </remarks>
    public int[]? HoldsMs { get; }

    /// <summary>How long to wait after pressing the key at this position.</summary>
    public int DwellFor(int index) =>
        HoldsMs != null && index < HoldsMs.Length ? HoldsMs[index] : IntervalMs;

    /// <summary>
    /// How long a weapon must stay in hand to be guaranteed to fire.
    /// </summary>
    /// <remarks>
    /// Swapping to a weapon does not arm it. Roblox plays an equip animation
    /// first, and a click that lands during it does nothing — so a rotation
    /// faster than the animation produces a weapon that is drawn over and over
    /// and never fired.
    ///
    /// Derived rather than guessed. The equip is a property of the game; the
    /// click period is a property of the clicker, which this app already knows.
    /// Two clicks rather than one because the first can land in the same
    /// instant the equip completes and be swallowed by the boundary — the
    /// second is what makes "every time" true instead of "usually".
    /// </remarks>
    public static int MinimumDwellMs(double clickPeriodMs, int equipMs = DefaultEquipMs, int clicks = 2)
    {
        if (double.IsNaN(clickPeriodMs) || clickPeriodMs <= 0) clickPeriodMs = 100;

        double needed = equipMs + clickPeriodMs * clicks;

        return (int)Math.Clamp(Math.Ceiling(needed), MinIntervalMs, MaxIntervalMs);
    }

    /// <summary>
    /// How long Roblox takes to put a weapon in your hand.
    /// </summary>
    /// <remarks>
    /// Measured off a recording of someone doing it by hand: the crossbow was
    /// dipped to for 130 to 200 milliseconds and fired reliably every time, so
    /// the equip has to cost well under that. An earlier guess of 250 was wrong
    /// by roughly four times and would have forced a rotation slower than the
    /// hand it was meant to copy.
    /// </remarks>
    public const int DefaultEquipMs = 60;

    public string Name { get; }

    /// <summary>Virtual key codes, sent in order and then from the top again.</summary>
    public int[] Keys { get; }

    /// <summary>The keys as the user typed them, for the card and for editing.</summary>
    public string KeysText { get; }

    public int IntervalMs { get; }

    public bool IsUsable => Keys.Length > 0 && Name.Trim().Length > 0;

    /// <summary>What the card says under the name.</summary>
    public string RateText => IntervalMs >= 1000
        ? $"every {IntervalMs / 1000.0:0.##}s"
        : $"every {IntervalMs} ms";

    public string SummaryText => Keys.Length > 1
        ? $"{KeysText}  ·  {RateText}  ·  cycles"
        : $"{KeysText}  ·  {RateText}";
}

/// <summary>
/// Sends the keys, on its own thread, until told to stop.
/// </summary>
/// <remarks>
/// A thread per running macro rather than one scheduler. Nobody runs twenty of
/// these — two is the realistic maximum — and a scheduler would be more code
/// to get wrong for no benefit anyone would notice.
/// </remarks>
public sealed class MacroRunner : IDisposable
{
    private readonly IKeyEngine _engine;

    private readonly Dictionary<string, (CancellationTokenSource Cts, KeyMacro Macro)> _running = new();

    private long _sent;

    public MacroRunner(IKeyEngine engine) => _engine = engine;

    public bool IsRunning(string name) => _running.ContainsKey(name);

    public int RunningCount => _running.Count;

    /// <summary>
    /// Every key code any currently running macro might send.
    /// </summary>
    /// <remarks>
    /// Exists so a hotkey watcher can tell its own macro's output apart from a
    /// person's keypress. On macOS the watcher reads key state off the same
    /// HID source this app's own synthetic presses go through (see
    /// <c>MacHotkeyWatcher.Poll</c>'s remarks), so a macro that types the same
    /// key a fixed hotkey is bound to — the switcher cycling 1 and 2 while a
    /// clicker hotkey is bound to 1, say — would otherwise retrigger that
    /// hotkey on every cycle. A stopped or disabled macro contributes nothing
    /// here: gone from <c>_running</c> means gone from this, the same rule
    /// a macro's own toggle hotkey already follows once it stops.
    /// </remarks>
    public IEnumerable<int> RunningKeys() => _running.Values.SelectMany(e => e.Macro.Keys);

    /// <summary>How many key presses have actually gone out.</summary>
    /// <remarks>
    /// Counts sends, not ticks. A macro that is running but suppressed reads
    /// zero here, which is the difference between "it is not working" and "it
    /// is working and you are looking at the wrong window".
    /// </remarks>
    public long Sent => Interlocked.Read(ref _sent);

    /// <summary>
    /// Asked before every press. True means skip this one.
    /// </summary>
    /// <remarks>
    /// This exists because a macro types into whatever is focused, and while
    /// somebody is setting one up that is this application — the keys land in
    /// the very boxes being edited, whose change handler restarts the macro,
    /// and it spends its life fighting itself instead of reaching the game.
    ///
    /// Called on the macro thread, so whatever is behind it must be safe to
    /// call from anywhere.
    ///
    /// Currently unassigned in this build: nothing sets this property, so it
    /// always reads null and every macro sends unconditionally regardless of
    /// what is focused. The Windows original wires it to a foreground-window
    /// check; wiring the same thing here needs a macOS frontmost-window check,
    /// which this port has not added. Do not assume a macro is suppressed
    /// while some other window has focus until something sets this.
    /// </remarks>
    public Func<bool>? Suppressed { get; set; }

    /// <summary>
    /// The clicker's input gate, so a key never lands inside a click.
    /// </summary>
    /// <remarks>
    /// The same lock the shake engine takes, and for the same reason. The
    /// clicker holds it between a mouse-down and its release; anything injected
    /// in that window arrives mid-click, and the game sees an interrupted press
    /// rather than a click and a keystroke.
    ///
    /// It is why switching by hand fires fast and switching automatically does
    /// not: a human presses the key between clicks by luck of timing, and a
    /// timer lands wherever it lands.
    /// </remarks>
    public object? InputGate { get; set; }

    /// <summary>
    /// How long a key is held down.
    /// </summary>
    /// <remarks>
    /// The same problem HitFix solves for the mouse. A press that goes down and
    /// up in the same instant falls between two frames of a game reading input
    /// once a frame, and is never observed at all — the switch either does not
    /// happen or happens unreliably. Fifteen milliseconds clears a 60fps frame
    /// and is far too short to notice.
    /// </remarks>
    private const int HoldMs = 15;

    /// <summary>Longest to wait for a click to finish before going anyway.</summary>
    /// <remarks>
    /// Sent regardless on timeout, deliberately. A split click costs one click;
    /// a missed switch leaves the wrong weapon in hand, which costs the fight.
    /// </remarks>
    private const int GateWaitMs = 250;

    public void Start(KeyMacro macro)
    {
        // Refused here rather than only in the UI, so nothing can start a
        // disabled macro by any route — its key, its switch, or a restore on
        // launch. Disabled means it does not run, not that one button is grey.
        if (!macro.Enabled || !macro.IsUsable || _running.ContainsKey(macro.Name)) return;

        var cts = new CancellationTokenSource();
        _running[macro.Name] = (cts, macro);

        CancellationToken token = cts.Token;

        new Thread(() => Loop(macro, token))
        {
            IsBackground = true,
            // Matched to the click engine. At default priority this thread was
            // descheduled under load, which made its sleeps overrun, which made
            // it spin longer to catch up — the stutter fed itself.
            Priority = ThreadPriority.AboveNormal,
            Name = "Macro:" + macro.Name
        }.Start();

        Changed?.Invoke();
    }

    /// <summary>
    /// Raised whenever the set of running macros changes.
    /// </summary>
    /// <remarks>
    /// So that anything reflecting that state cannot fall out of step with it.
    /// Macros are started and stopped from a dozen places — a card's switch, a
    /// hotkey on the polling thread, the master kill, deleting a macro, closing
    /// the app — and an on-screen badge still claiming a macro is running after
    /// it stopped is worse than showing nothing at all.
    ///
    /// Raised on whichever thread made the change, including the poll thread,
    /// so a UI handler has to marshal.
    /// </remarks>
    public event Action? Changed;

    public void Stop(string name)
    {
        if (!_running.TryGetValue(name, out (CancellationTokenSource Cts, KeyMacro Macro) entry)) return;

        entry.Cts.Cancel();
        _running.Remove(name);

        Changed?.Invoke();
    }

    public void StopAll()
    {
        if (_running.Count == 0) return;

        foreach (var entry in _running.Values) entry.Cts.Cancel();

        _running.Clear();

        Changed?.Invoke();
    }

    private void Loop(KeyMacro macro, CancellationToken token)
    {
        int at = 0;

        // The one key that might still be down mid-send. SendGated always
        // pairs its own down and up, so this is cleared the instant it
        // returns normally — it is only ever read by the rescue below, for
        // the case where a send throws between the two.
        int? held = null;

        // The Windows build raised the system timer resolution and opted out
        // of background throttling here, so a sleep could land within a
        // millisecond instead of the scheduler's default ~15.6 ms tick. Both
        // calls were Win32-specific (winmm's TimeBeginPeriod and a Windows-only
        // process throttling API) and are not carried across.
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Suppressed rather than paused: the cycle still advances, so
                // alt-tabbing away and back does not leave it stuck on one key.
                if (Suppressed?.Invoke() != true)
                {
                    int code = macro.Keys[at];
                    held = code;
                    SendGated(code, token);
                    held = null;
                    Interlocked.Increment(ref _sent);
                }

                int dwell = macro.DwellFor(at);
                bool firing = at == 0 && macro.ClicksWanted > 0 && Clicks != null;

                at = (at + 1) % macro.Keys.Length;

                if (firing) WaitForShots(macro, dwell, token);
                else Wait(dwell, token);
            }
        }
        catch
        {
            // A failed send must not take the thread — or the app — with it.
        }
        finally
        {
            // The one that was pressed, if a send failed before its own
            // release ran. Matches what Clicker.Loop does for the mouse
            // button, and for the same reason: a key left down in a game is
            // the keyboard equivalent of a button stuck across the desktop.
            //
            // This release only runs because a send already threw, and the
            // usual reason — Accessibility permission revoked mid-run — is
            // still true here, so the release is likely to throw the same
            // way. Guarded because that second throw has nowhere left to go
            // but out of this background thread, which kills the process —
            // exactly what the catch above exists to prevent.
            if (held is int stuck)
            {
                try { Gated(() => _engine.KeyUp(stuck)); }
                catch
                {
                    // See above: the release can fail for the same reason
                    // the send did, and must not be allowed to take the
                    // process down with it.
                }
            }
        }
    }

    /// <summary>
    /// The clicker's running total of delivered clicks.
    /// </summary>
    /// <remarks>
    /// Read, never written. It is what lets a dip end when the weapon has
    /// actually fired rather than when a stopwatch says it probably has.
    /// </remarks>
    public Func<long>? Clicks { get; set; }

    /// <summary>
    /// Waits out a span accurately, rather than approximately.
    /// </summary>
    /// <remarks>
    /// Thread.Sleep returns when the scheduler next gets round to it, not when
    /// the time is up. Even with the timer at 1 ms each call overshoots, and a
    /// dwell slept in slices compounds every one of them — a 150 ms crossbow
    /// dip built from thirty 5 ms sleeps measured 165-180 ms, long enough to
    /// cost the swing it was supposed to leave time for.
    ///
    /// So the bulk is slept a millisecond at a time and the last stretch is
    /// spun. How long that stretch needs to be depends on the system timer,
    /// which this cannot assume anything about — the loop raises it to 1 ms,
    /// but a caller outside that, a test included, gets the ~15.6 ms default.
    /// So the tail is not a constant: each sleep is measured, and the longest
    /// one seen becomes the distance at which sleeping stops being safe. A
    /// coarse timer teaches it on the first sleep and costs one overshoot.
    /// </remarks>
    /// <returns>False if cancellation was observed.</returns>
    internal static bool Wait(double ms, CancellationToken token)
    {
        if (ms <= 0) return !token.IsCancellationRequested;

        long freq = Stopwatch.Frequency;
        long deadline = Stopwatch.GetTimestamp() + (long)(ms * freq / 1000.0);
        double tail = SpinTailMs;

        var spin = new SpinWait();

        while (true)
        {
            if (token.IsCancellationRequested) return false;

            long now = Stopwatch.GetTimestamp();
            double remaining = (deadline - now) * 1000.0 / freq;
            if (remaining <= 0) return true;

            if (remaining <= tail)
            {
                // SpinOnce rather than a bare SpinWait: it starts by spinning
                // and then begins yielding, so a wait that runs long gives the
                // core back to the game instead of holding it. The -1 disables
                // its escalation to Sleep(1), which would overshoot by more
                // than the whole tail it is trying to land inside.
                spin.SpinOnce(sleep1Threshold: -1);
                continue;
            }

            spin.Reset();

            Thread.Sleep(1);

            // What that sleep actually cost, which is the floor on how close a
            // sleep can get us. Anything nearer than this has to be spun.
            //
            // Capped, and the cap is the fix for a real problem: this only ever
            // grew. One slow sleep — a scheduler hiccup, or the timer not raised
            // yet at 15.6 ms — set the tail to that, and from then on every
            // interval shorter than it was spun end to end. A macro with a short
            // dwell would burn a whole core for as long as it ran, which is what
            // the in-game stutter was. Past the cap it is better to overshoot a
            // press slightly than to take a core off the game.
            double slept = (Stopwatch.GetTimestamp() - now) * 1000.0 / freq;
            if (slept > tail) tail = Math.Min(slept, MaxSpinTailMs);
        }
    }

    /// <summary>
    /// The most of an interval that may be spent spinning.
    /// </summary>
    /// <remarks>
    /// With the timer raised a sleep lands within about a millisecond, so this
    /// is roughly twice what is ever needed. It exists to bound the damage when
    /// a sleep does not land — not to be reached in normal running.
    /// </remarks>
    private const double MaxSpinTailMs = 2.0;

    /// <summary>
    /// Shortest stretch spun rather than slept, before measurement widens it.
    /// </summary>
    private const double SpinTailMs = 1.2;

    /// <summary>
    /// Stays on the weapon until it has actually been clicked, then leaves.
    /// </summary>
    /// <remarks>
    /// As short as it can be while still firing, which is the whole point. Time
    /// on the crossbow is time the sword is not swinging, and this game is
    /// scored in hits — so the dip ends on the click that fires it rather than
    /// on a timer that has to be generous to be safe.
    ///
    /// The configured hold is a ceiling, not a target. If the clicks never
    /// arrive — clicker switched off, a click rate slower than the hold — it
    /// gives up and moves on rather than parking on one weapon for ever.
    /// </remarks>
    private void WaitForShots(KeyMacro macro, int ceilingMs, CancellationToken token)
    {
        if (!Wait(macro.EquipMs, token)) return;

        long from = Clicks?.Invoke() ?? 0;
        int remaining = Math.Max(0, ceilingMs - macro.EquipMs);
        long deadline = Stopwatch.GetTimestamp() + (long)(remaining * Stopwatch.Frequency / 1000.0);

        // Polled fine enough that the dip ends on the click rather than up to a
        // slice after it. At 40 clicks a second they arrive 25 ms apart, so a
        // millisecond of detection lag is the difference between leaving on the
        // shot and leaving a slice late, every single swap.
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (token.IsCancellationRequested) return;

            if ((Clicks?.Invoke() ?? 0) - from >= macro.ClicksWanted) return;

            Thread.Sleep(1);
        }
    }

    /// <summary>
    /// Presses the key without splitting a click in half.
    /// </summary>
    /// <remarks>
    /// Takes the clicker's gate if there is one, so the whole press and release
    /// happens between two clicks rather than inside one. Waiting costs a few
    /// milliseconds on a switch that happens twice a second; not waiting costs
    /// the click it lands in the middle of.
    /// </remarks>
    private void SendGated(int code, CancellationToken token)
    {
        Gated(() => _engine.KeyDown(code));

        // Outside the gate. The clicker is free to click while a key is held —
        // that is just clicking with a key down, which is what a hand does.
        Wait(HoldMs, token);

        Gated(() => _engine.KeyUp(code));
    }

    /// <summary>
    /// Runs one send between clicks rather than inside one.
    /// </summary>
    /// <remarks>
    /// Around each individual event, never across the hold between them. Held
    /// for the whole press this blocks the click loop for the full hold time on
    /// every swap — three percent of clicks at a half-second rotation, and a
    /// quarter of them at the fast rates the switch technique actually wants.
    /// A feature that costs hits in a game measured in hits is worse than no
    /// feature, so the lock is taken for microseconds and released.
    /// </remarks>
    private void Gated(Action send)
    {
        object? gate = InputGate;

        if (gate == null)
        {
            send();
            return;
        }

        bool held = Monitor.TryEnter(gate, GateWaitMs);

        try
        {
            send();
        }
        finally
        {
            if (held) Monitor.Exit(gate);
        }
    }

    public void Dispose() => StopAll();
}

/// <summary>
/// The saved macros, kept beside the click presets.
/// </summary>
public static class MacroStore
{
    private static readonly string MacrosFile = SettingsPath.For("macros.json");

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

    private sealed class StoredMacro
    {
        public string Name { get; set; } = "";
        public int[] Keys { get; set; } = Array.Empty<int>();
        public string KeysText { get; set; } = "";
        public int IntervalMs { get; set; } = 100;
        // Zero means unbound. Absent from files written before this feature
        // shipped, which defaults them to unbound on load — no migration.
        public int HotkeyCode { get; set; }
        public string HotkeyName { get; set; } = "";

        // Absent from files written before this shipped, and a missing bool
        // reads as false — which would silently disable every existing macro.
        // Stored as "Disabled" so the old files' absence means enabled.
        public bool Disabled { get; set; }
    }

    /// <summary>
    /// The file on disk: which platform wrote it, alongside the macros
    /// themselves. An object rather than a bare array so the platform tag has
    /// somewhere to live without hijacking the macro list's own shape.
    /// </summary>
    private sealed class StoredFile
    {
        [JsonPropertyName("platform")]
        public string? Platform { get; set; }

        [JsonPropertyName("macros")]
        public List<StoredMacro> Macros { get; set; } = new();
    }

    /// <summary>
    /// Nothing. A fresh install starts with an empty list.
    /// </summary>
    /// <remarks>
    /// Shipping example macros was a mistake worth naming: they arrive looking
    /// like features the app provides rather than things the user made, and the
    /// first instinct is to delete them. A macro is only useful if it is one
    /// somebody chose.
    /// </remarks>
    public static List<KeyMacro> Defaults() => new();

    /// <summary>
    /// The auto switcher used to live in this list before it became its own
    /// page. Anyone who ran that build has it saved, and it would show up twice.
    /// </summary>
    private static bool IsLegacySwitcher(string name) =>
        name.Trim().Equals("Auto Switcher", StringComparison.OrdinalIgnoreCase);

    public static List<KeyMacro> Load()
    {
        try
        {
            if (!File.Exists(MacrosFile)) return Defaults();

            var file = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(MacrosFile));
            if (file == null) return Defaults();

            // A file whose platform is missing or is anything other than this
            // build's own is treated as if it held no macros at all — never
            // partially loaded, never an error. See PlatformTag for why.
            if (file.Platform != PlatformTag) return Defaults();

            return file.Macros
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .Where(m => !IsLegacySwitcher(m.Name))
                .Select(m => new KeyMacro(
                    m.Name, m.Keys, m.KeysText, m.IntervalMs,
                    hotkey: m.HotkeyCode > 0 ? new HotkeyBinding(m.HotkeyCode, m.HotkeyName) : HotkeyBinding.Unbound,
                    enabled: !m.Disabled))
                .ToList();
        }
        catch
        {
            return Defaults();
        }
    }

    public static void Save(IEnumerable<KeyMacro> macros)
    {
        try
        {
            var stored = macros
                .Select(m => new StoredMacro
                {
                    Name = m.Name,
                    Keys = m.Keys,
                    KeysText = m.KeysText,
                    IntervalMs = m.IntervalMs,
                    HotkeyCode = m.Hotkey.Code,
                    HotkeyName = m.Hotkey.Name,
                    Disabled = !m.Enabled
                })
                .ToList();

            var file = new StoredFile { Platform = PlatformTag, Macros = stored };

            File.WriteAllText(MacrosFile,
                JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>Adds a macro, or replaces the one that already has that name.</summary>
    public static void Upsert(List<KeyMacro> macros, KeyMacro macro)
    {
        int existing = macros.FindIndex(m =>
            string.Equals(m.Name, macro.Name, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0) macros[existing] = macro;
        else macros.Add(macro);
    }

    /// <summary>The macro already saved under this name, or null when none is.</summary>
    /// <remarks>
    /// Same case-insensitive match <see cref="Upsert"/> replaces by. Exposed
    /// separately so the page can find out *before* calling Upsert that a
    /// replacement is about to happen, without Upsert itself having to report
    /// what it clobbered.
    /// </remarks>
    public static KeyMacro? Find(IEnumerable<KeyMacro> macros, string name) =>
        macros.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The hotkey Save should actually write, and the line to show for it,
    /// when the typed name matches a macro that already exists.
    /// </summary>
    /// <remarks>
    /// <see cref="Upsert"/> replaces a same-named macro wholesale — including
    /// its hotkey — and that is deliberate, it is what makes editing work. The
    /// New Macro form's own hotkey slot defaults to
    /// <see cref="HotkeyBinding.Unbound"/> though, so saving under an existing
    /// name with nothing picked in that slot would otherwise silently wipe a
    /// working hotkey: the macro would look fine and its key would do nothing.
    ///
    /// When that is the situation, the existing hotkey is carried over instead
    /// of lost — almost certainly what re-saving under the same name is for —
    /// and the notice says so, so the carry-over itself is not a surprise
    /// either. When the form *did* pick a hotkey, that pick wins, same as any
    /// other field on the replacement.
    /// </remarks>
    public static (HotkeyBinding Hotkey, string? Notice) ResolveSaveHotkey(KeyMacro? existing, HotkeyBinding pending)
    {
        if (existing == null) return (pending, null);

        if (!pending.IsValid && existing.Hotkey.IsValid)
        {
            return (existing.Hotkey,
                $"\"{existing.Name}\" already existed — replaced it, and kept its {existing.Hotkey.Name} "
                + "hotkey since this form's hotkey slot was empty.");
        }

        if (pending.IsValid && existing.Hotkey.IsValid && pending.Code != existing.Hotkey.Code)
        {
            return (pending,
                $"\"{existing.Name}\" already existed — replaced it, including its hotkey "
                + $"({existing.Hotkey.Name} to {pending.Name}).");
        }

        return (pending, $"\"{existing.Name}\" already existed — replaced it.");
    }

    /// <summary>The macro that owns this hotkey code, or null when none does.</summary>
    /// <remarks>
    /// Matched by code alone, disabled macros included — the same rule
    /// <c>MainWindow.Macros.cs</c>'s own <c>HotkeyHolder</c> already applies
    /// when checking one macro's pick against every other one. A disabled
    /// macro's key still belongs to it; letting something else take it while
    /// it is merely disabled would collide the moment it is re-enabled.
    ///
    /// This is what makes the fixed hotkeys' <c>Bind</c> and the macros'
    /// <c>BindMacroHotkey</c> agree: both refuse a key a macro already owns,
    /// instead of only one of them checking.
    /// </remarks>
    public static KeyMacro? FindByHotkeyCode(IEnumerable<KeyMacro> macros, int code, KeyMacro? excluding = null) =>
        macros.FirstOrDefault(m => !ReferenceEquals(m, excluding) && m.Hotkey.IsValid && m.Hotkey.Code == code);

    /// <summary>
    /// A can't be bound on this build — see <see cref="HotkeyBinding.Unbound"/>
    /// for why its own code doubles as "no key at all" on macOS. Shared by
    /// every place that can hit it: a typed KEY box on the Macros page, a
    /// captured toggle hotkey, a fixed hotkey, and a switcher slot.
    /// </summary>
    public const string UnbindableAMessage =
        "A can't be bound on this build. Its key code doubles as this platform's \"no key\" marker, "
        + "so the app can't tell a bound A from none at all — pick a different letter.";

    /// <summary>Whether any comma/space-separated piece of typed text is the letter A.</summary>
    /// <remarks>
    /// Lets a caller tell "nothing usable was typed" apart from "the one
    /// unbindable letter was typed", so it can show <see cref="UnbindableAMessage"/>
    /// instead of a generic validation error that would be untrue of A
    /// specifically.
    /// </remarks>
    public static bool MentionsUnbindableA(string? typed) =>
        (typed ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(piece => piece.Trim().Equals("A", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads keys typed as "1, 2" or "R" into the running platform's own key
    /// codes.
    /// </summary>
    /// <remarks>
    /// Letters and digits only, which covers every hotbar slot and every action
    /// key in the game this is for. Accepting the whole keyboard would mean
    /// parsing "Left Shift" and deciding what a macro that holds a modifier
    /// even means.
    ///
    /// "The platform's own key codes", not "virtual key codes" — Windows and
    /// macOS number the same keys differently (see <see cref="KeyCodes"/>),
    /// and this used to say the Windows-only name for what it produces.
    /// </remarks>
    public static (int[] Keys, string Text)? ParseKeys(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        var keys = new List<int>();
        var names = new List<string>();

        // The separators as an explicit array. Written as Split(',', ' ', options)
        // it binds to the (char, int count, options) overload instead — a space
        // converts to int silently — and splits on commas only, with a limit of
        // thirty-two. It compiles, and "1 2" quietly parses as one key.
        char[] separators = { ',', ' ' };

        foreach (string piece in typed.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            string one = piece.Trim().ToUpperInvariant();

            if (one.Length != 1) return null;

            char c = one[0];

            if (!char.IsLetterOrDigit(c)) return null;

            if (KeyCodes.For(c) is not int code) return null;

            keys.Add(code);
            names.Add(one);
        }

        return keys.Count == 0 ? null : (keys.ToArray(), string.Join(", ", names));
    }

    /// <summary>Reads an interval, or null when it is not one.</summary>
    public static int? ParseInterval(string? typed)
    {
        if (!int.TryParse(typed?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int ms)
            && !int.TryParse(typed?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms))
        {
            return null;
        }

        return ms is < KeyMacro.MinIntervalMs or > KeyMacro.MaxIntervalMs ? null : ms;
    }
}
