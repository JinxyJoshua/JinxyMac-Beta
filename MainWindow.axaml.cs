using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using JinxyMac.Capture;
using JinxyMac.Core;
using JinxyMac.Engine;

namespace JinxyMac;

public partial class MainWindow : Window
{
    private readonly IClickEngine _engine;
    private readonly IHotkeyWatcher _hotkeys;
    private readonly Clicker _clicker;
    private readonly Shaker _shaker;
    private readonly ScreenRecorder _recorder = new();
    private readonly ReplayBuffer _replay = new();
    private readonly List<CaptureDevice> _screens = new();

    /// <summary>The menu bar item, while it is showing.</summary>
    private TrayIcon? _tray;
    private NativeMenuItem? _trayToggle;

    /// <summary>What the tray icon currently shows, so it is only redrawn on a change.</summary>
    private (bool Running, Color Colour)? _trayLook;
    private readonly DispatcherTimer _stats;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ClickHistory _history = ClickHistory.Load();
    private readonly List<ClickPreset> _presets = PresetStore.Load();

    /// <summary>The preset cards, so the applied one can be lit without a rebuild.</summary>
    private readonly List<(ClickPreset Preset, Border Card, Border Rail)> _presetCards = new();

    /// <summary>Re-render actions for the typed readouts, one per slider.</summary>
    private readonly List<Action> _readouts = new();

    private int _fps = 60;
    private int _replaySeconds = 30;

    /// <summary>The screen being captured, or null when none was found.</summary>
    private CaptureDevice? _screen;

    /// <summary>The clip the uploader is pointed at, if any.</summary>
    private string? _clip;

    /// <summary>The most recently saved clip, for the Show in Finder button.</summary>
    private string? _lastClip;
    private bool _historyDirty;
    private bool _valuesHidden;

    /// <summary>The preset the pencil loaded into the custom row, if any.</summary>
    private ClickPreset? _editing;

    /// <summary>The shake values the Save button kept, for Restore to put back.</summary>
    private (double Left, double Right, double Up, double Down, double Speed)? _savedShake;

    /// <summary>Suppressed while settings are being applied, so restoring a
    /// value does not immediately look like the user changing it.</summary>
    private bool _loading;

    private long _lastClicks;
    private DateTime _lastTick = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();

        // The whole reason the engine is behind an interface: on Windows this
        // app is fully usable, so every page can be judged before a Mac is ever
        // involved. Only the macOS implementation is unverifiable here.
        if (OperatingSystem.IsMacOS())
        {
            _engine = new MacClickEngine();
            _hotkeys = new MacHotkeyWatcher();
        }
        else if (OperatingSystem.IsWindows())
        {
            _engine = new WindowsClickEngine();
            _hotkeys = new WindowsHotkeyWatcher();
        }
        else
        {
            // Written as if/else rather than a conditional so the platform
            // checks are ones the compiler can see. Both implementations call
            // into an OS-specific library; picking one on a third platform
            // would fail later and less clearly than it does here.
            throw new PlatformNotSupportedException(
                "This build runs on macOS and Windows.");
        }

        _clicker = new Clicker(_engine);

        // Shares the clicker gate, which is what stops a shake landing between
        // a press and its release and turning the click into a drag.
        _shaker = new Shaker(_engine, _clicker.InputGate);

        WireNavigation();
        WireClicker();
        WireShake();
        WireHotkey();
        WireRecorder();
        WireHistory();
        WirePresets();
        WireTheme();
        WireSettings();
        WireCache();
        WireMenuBar();
        WireUpdates();
        DescribeEngine();
        ApplySettings();

        // After ApplySettings, which is what decides the screen to select.
        _ = StartCapture();
        RefreshMenuBar();

        // Config first, so anything it turns off is off before the update
        // prompt or any feature has had a chance to run. Fire and forget: the
        // shipped defaults are already in force, and nothing waits on this.
        _ = RemoteConfig.LoadAsync(CancellationToken.None);

        // Quietly, and only if asked for. Nothing is downloaded without a press.
        if (_settings.AutoCheckUpdates) _ = CheckForUpdate(announce: false);

        _stats = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _stats.Tick += (_, _) => UpdateMeasured();
        _stats.Start();

        Closed += (_, _) =>
        {
            Persist();
            FlushHistory();

            // Before the engines: a killed ffmpeg leaves an MP4 with no index,
            // and the stop is graceful precisely to avoid that.
            HideTray();

            _recorder.Dispose();
            _replay.Dispose();
            _clicker.Dispose();
            _shaker.Dispose();
            _hotkeys.Dispose();
        };

        _hotkeys.Start();

        Publish();
    }

    // ---- navigation ----

    private void WireNavigation()
    {
        Wire(NavClicker, PageClicker, "Clicker", "Configure your own click engine");
        Wire(NavPresets, PagePresets, "Presets", "Saved click configurations");
        Wire(NavRecorder, PageRecorder, "Recorder", "Screen capture, hardware encoded");
        Wire(NavHistory, PageHistory, "History", "Time spent clicking, and how much of it landed");
        Wire(NavTheme, PageTheme, "Theme", "Accent colour");
        Wire(NavSettings, PageSettings, "Settings", "Where things are stored, and what this build can do");

        void Wire(RadioButton button, Control page, string title, string subtitle) =>
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true) Show(page, title, subtitle);
            };
    }

    private void Show(Control page, string title, string subtitle)
    {
        PageClicker.IsVisible = ReferenceEquals(page, PageClicker);
        PagePresets.IsVisible = ReferenceEquals(page, PagePresets);
        PageRecorder.IsVisible = ReferenceEquals(page, PageRecorder);
        PageHistory.IsVisible = ReferenceEquals(page, PageHistory);
        PageTheme.IsVisible = ReferenceEquals(page, PageTheme);
        PageSettings.IsVisible = ReferenceEquals(page, PageSettings);

        PageTitleText.Text = title;
        PageSubtitleText.Text = subtitle;

        // The status pill and start button belong to the clicker. Left on every
        // page they would offer to start it from the Theme page, which is not
        // what a header is for.
        StatusBlock.IsVisible = ReferenceEquals(page, PageClicker);

        // Rebuilt on arrival rather than on every tick. The totals change while
        // the clicker runs, and redrawing sixty rows a second to show a page
        // nobody is looking at is work for nothing.
        if (ReferenceEquals(page, PageHistory)) RefreshHistory();
    }

    // ---- clicker ----

    private void WireClicker()
    {
        BindReadout(CpsSlider, CpsValue, "0.00");
        BindReadout(DutySlider, DutyValue, "0.00");

        CpsSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) Publish();
        };

        DutySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) Publish();
        };

        HitFixToggle.IsCheckedChanged += (_, _) => Publish();
        SpinToggle.IsCheckedChanged += (_, _) => Publish();

        ButtonLeft.IsCheckedChanged += (_, _) => Publish();
        ButtonRight.IsCheckedChanged += (_, _) => Publish();
        ButtonMiddle.IsCheckedChanged += (_, _) => Publish();

        StartStopButton.Click += (_, _) => Toggle();

        HoldModeButton.IsCheckedChanged += (_, _) => ModeChanged();
        ToggleModeButton.IsCheckedChanged += (_, _) => ModeChanged();

        HideValuesButton.Click += (_, _) => HideValues(!_valuesHidden);

        RecheckAccessButton.Click += (_, _) =>
        {
            RefreshPermissionBanner();
            DescribePermissions();
        };

        OpenAccessibilityButton.Click += (_, _) =>
            Open("x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility");
    }

    /// <summary>
    /// Shows or hides the banner explaining why nothing is being clicked.
    /// </summary>
    /// <remarks>
    /// Driven off the engine rather than a stored flag, so it disappears the
    /// moment the permission is granted rather than at the next restart.
    ///
    /// This is the failure worth the most words in the whole app. Everything
    /// looks like it is working — the button says RUNNING, the counter counts,
    /// the measured rate is right — and macOS is throwing every event away.
    /// Someone who does not know the permission exists has no way to guess.
    /// </remarks>
    private void RefreshPermissionBanner()
    {
        bool blocked = !_engine.IsAvailable;

        PermissionBanner.IsVisible = blocked;

        if (!blocked) return;

        PermissionReasonText.Text = _engine.Unavailable
            ?? "Clicks are not reaching the system.";
    }

    /// <summary>
    /// Switching mode stops the clicker rather than carrying it over.
    /// </summary>
    /// <remarks>
    /// Leaving it running across the change means hold mode inherits a latched
    /// session it has no key held for, and nothing short of the button will
    /// stop it.
    /// </remarks>
    private void ModeChanged()
    {
        if (_loading) return;

        if (_clicker.IsRunning) Toggle();

        _settings.HoldMode = HoldModeButton.IsChecked == true;
        _settings.Save();
    }

    /// <summary>
    /// Masks the rates, for recording or streaming.
    /// </summary>
    /// <remarks>
    /// The boxes are disabled rather than merely overwritten. A masked box that
    /// still takes input would commit whatever was typed over the mask the
    /// moment focus left it, silently changing the rate it was hiding.
    /// </remarks>
    private void HideValues(bool hidden)
    {
        _valuesHidden = hidden;

        HideValuesButton.Content = hidden ? "Show values" : "Hide values";

        CpsValue.IsEnabled = !hidden;
        DutyValue.IsEnabled = !hidden;

        if (hidden)
        {
            CpsValue.Text = "•••";
            DutyValue.Text = "•••";
        }
        else
        {
            RefreshReadouts();
        }
    }

    /// <summary>
    /// Ties a readout to its slider in both directions, so the number can be
    /// typed as well as dragged.
    /// </summary>
    /// <remarks>
    /// A slider is the wrong instrument for a rate that matters to two decimal
    /// places. Dragging to exactly 85.86 on a track a thousand wide is not
    /// something anyone should have to do, and the presets are written to that
    /// precision — so the number has to be reachable by typing it.
    ///
    /// The slider stays the source of truth. Typing sets it and the text is then
    /// re-rendered from what the slider actually took, which is what makes an
    /// out-of-range or unreadable entry snap back rather than sit there looking
    /// accepted.
    /// </remarks>
    private void BindReadout(Slider slider, TextBox box, string format)
    {
        void Render()
        {
            // A masked box is left alone. Rewriting it from the slider is
            // exactly the leak Hide values exists to prevent.
            if (!box.IsEnabled) return;

            box.Text = slider.Value.ToString(format, CultureInfo.CurrentCulture);
        }

        void Commit()
        {
            string typed = box.Text?.Trim() ?? "";

            if (double.TryParse(typed, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
                || double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                if (!double.IsNaN(value))
                    slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
            }

            Render();
        }

        // Not while it is being typed into, or the caret jumps to the end of a
        // reformatted string after every keystroke.
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !box.IsFocused) Render();
        };

        // Selected on focus so typing replaces the number rather than landing
        // in the middle of it.
        box.GotFocus += (_, _) => box.SelectAll();
        box.LostFocus += (_, _) => Commit();

        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            Commit();
            e.Handled = true;
        };

        _readouts.Add(Render);
    }

    /// <summary>Re-renders every typed readout from its slider.</summary>
    /// <remarks>
    /// Needed after settings load: a restored value that happens to equal the
    /// one already in the XAML raises no change event, so nothing would redraw
    /// it and a differently-formatted default would linger.
    /// </remarks>
    private void RefreshReadouts()
    {
        foreach (Action render in _readouts) render();
    }

    private void Toggle()
    {
        if (_clicker.IsRunning)
        {
            _building = false;
            _clicker.Stop();

            // A session's worth of totals reaches the disk when the session
            // ends, rather than once a second while it runs.
            FlushHistory();
        }
        else
        {
            if (!_engine.IsAvailable)
            {
                // The pill is one word; the banner is the explanation, and
                // this is the moment it is most wanted.
                StatusText.Text = "BLOCKED";
                RefreshPermissionBanner();
                NavClicker.IsChecked = true;
                return;
            }

            // The ordinary key means the sliders. Starting with the building
            // rate still armed from a previous run would click at 35 while the
            // page said otherwise.
            _building = false;
            Publish();

            _clicker.Start();
        }

        // Shake follows the clicker rather than running on its own.
        RefreshShake();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        bool running = _clicker.IsRunning;

        StatusText.Text = running ? (_building ? "BUILDING" : "RUNNING") : "IDLE";

        // The key on the button, the way the Windows build shows it, so the
        // binding is readable without looking at the card below.
        string key = _settings.HotkeyCode == 0 ? "" : _settings.HotkeyName + "  ";

        StartStopButton.Content = key + (running ? "STOP" : "START");

        StatusDot.Fill = running
            ? this.FindResource("Accent") as IBrush
            : this.FindResource("TextMuted") as IBrush;
    }

    /// <summary>
    /// Hands the sliders to the engine, and says so when HitFix is overriding
    /// them rather than leaving the numbers to quietly mean nothing.
    /// </summary>
    private void Publish()
    {
        if (CpsSlider == null || DutySlider == null) return;

        double cps = CpsSlider.Value;
        double duty = Math.Clamp(DutySlider.Value / 100.0, 0, 1);
        bool hitFix = HitFixToggle.IsChecked == true;

        // Building substitutes its own rate for the sliders, and drops HitFix
        // with them — a floor on the press length is exactly the thing the fixed
        // rate is choosing for itself.
        _clicker.Apply(_building
            ? new ClickSettings(Clicker.BuildCps, Clicker.BuildDuty, false,
                                SpinToggle.IsChecked == true, SelectedButton)
            : new ClickSettings(cps, duty, hitFix,
                                SpinToggle.IsChecked == true, SelectedButton));

        if (_building)
        {
            ClampText.Text =
                $"Building is sending {Clicker.BuildCps:0} /s at {Clicker.BuildDuty * 100:0} %, "
                + "not what the sliders say.";
            ClampText.IsVisible = true;
        }
        else if (ClickTimings.IsClamped(cps, duty, hitFix))
        {
            ClickTiming sent = ClickTimings.Resolve(cps, duty, hitFix);

            ClampText.Text =
                $"HitFix is sending {sent.Cps:0.0} /s at {sent.DutyPercent:0} %, "
                + "not what the sliders say. Lower them until this matches.";
            ClampText.IsVisible = true;
        }
        else
        {
            ClampText.IsVisible = false;
        }

        // The highlight follows the sliders, so it has to be refreshed wherever
        // they land — by preset, by hand, or restored from settings.
        MarkApplied();

        if (!_loading) Persist();
    }

    /// <summary>The button the selector is on.</summary>
    private ClickButton SelectedButton =>
        ButtonRight.IsChecked == true ? ClickButton.Right
        : ButtonMiddle.IsChecked == true ? ClickButton.Middle
        : ClickButton.Left;

    private void UpdateMeasured()
    {
        long clicks = _clicker.ClickCount;
        DateTime now = DateTime.UtcNow;
        double seconds = (now - _lastTick).TotalSeconds;

        if (seconds > 0)
        {
            long delivered = clicks - _lastClicks;
            double rate = delivered / seconds;

            MeasuredText.Text = _clicker.IsRunning
                ? $"Measured {rate:0.0} /s"
                : "Measured — /s";

            // Classified from the same one-second delta the readout shows, so
            // the sentence and the number can never disagree.
            double setCps = _building ? Clicker.BuildCps : CpsSlider.Value;
            double duty = Math.Clamp(DutySlider.Value / 100.0, 0, 1);

            OutputState state = ClickOutput.Classify(
                _clicker.IsRunning,
                setCps,
                rate,
                hitFixClamping: !_building
                                && ClickTimings.IsClamped(setCps, duty, HitFixToggle.IsChecked == true));

            VerdictText.Text = ClickOutput.Verdict(state, setCps, rate);

            VerdictText.Foreground = ClickOutput.IsWarning(state)
                ? this.FindResource("Accent") as IBrush
                : this.FindResource("TextMuted") as IBrush;

            RecordActivity(seconds, delivered);
        }

        _lastClicks = clicks;
        _lastTick = now;

        RefreshStatus();
        RefreshRecording();
        RefreshTiles();
        RefreshTray();

        // Re-checked every second, so granting the permission clears the banner
        // without a restart and losing it puts the banner back.
        RefreshPermissionBanner();
    }

    /// <summary>The two machine tiles at the top of the page.</summary>
    private void RefreshTiles()
    {
        MachineLoad load = SystemLoad.Read();

        // The first reading has no previous sample to subtract, so it reports
        // nothing rather than a zero that would look like an idle machine.
        if (load.CpuPercent != null) CpuText.Text = load.CpuText;

        RamText.Text = load.RamText;
    }

    // ---- shake ----

    private void WireShake()
    {
        Watch(ShakeLeftSlider, ShakeLeftValue);
        Watch(ShakeRightSlider, ShakeRightValue);
        Watch(ShakeUpSlider, ShakeUpValue);
        Watch(ShakeDownSlider, ShakeDownValue);
        Watch(ShakeSpeedSlider, ShakeSpeedValue);

        ShakeToggle.IsCheckedChanged += (_, _) => RefreshShake();

        SaveShakeButton.Click += (_, _) =>
        {
            Persist();
            _savedShake = CurrentShake();
            RestoreShakeButton.IsEnabled = true;
        };

        // Disabled until something has been saved, so the button never offers
        // to restore values that were never kept.
        RestoreShakeButton.IsEnabled = false;

        RestoreShakeButton.Click += (_, _) =>
        {
            if (_savedShake is not (double left, double right, double up, double down, double speed))
                return;

            ShakeLeftSlider.Value = left;
            ShakeRightSlider.Value = right;
            ShakeUpSlider.Value = up;
            ShakeDownSlider.Value = down;
            ShakeSpeedSlider.Value = speed;
        };

        void Watch(Slider slider, TextBox readout)
        {
            // Whole pixels: these are distances the pointer jumps, and a
            // fractional one is not a thing the mouse can be asked for.
            BindReadout(slider, readout, "0");

            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty) RefreshShake();
            };
        }
    }

    /// <summary>
    /// Hands the ranges to the shake thread, and starts or stops it.
    /// </summary>
    /// <remarks>
    /// Shake only runs while the clicker does. On its own it would move the
    /// camera of someone who is not attacking, which is not what the toggle
    /// promises — and on the Windows build was a bug rather than a feature.
    /// </remarks>
    private void RefreshShake()
    {
        if (ShakeToggle == null || ShakeSpeedSlider == null) return;

        _shaker.Apply(
            new ShakeRange(
                ShakeLeftSlider.Value,
                ShakeRightSlider.Value,
                ShakeUpSlider.Value,
                ShakeDownSlider.Value),
            ShakeSpeedSlider.Value);

        // Says what the sliders currently mean, rather than repeating a fixed
        // sentence that stopped being true the moment one of them moved.
        ShakeStatusText.Text =
            $"Moves the camera up to {ShakeLeftSlider.Value:0} left, {ShakeRightSlider.Value:0} right, "
            + $"{ShakeUpSlider.Value:0} up and {ShakeDownSlider.Value:0} down, "
            + $"about {ShakeSpeedSlider.Value:0} times a second while the clicker runs.";

        bool wanted = ShakeToggle.IsChecked == true && _clicker.IsRunning;

        if (wanted) _shaker.Start();
        else _shaker.Stop();

        if (!_loading) Persist();
    }

    private (double, double, double, double, double) CurrentShake() =>
        (ShakeLeftSlider.Value, ShakeRightSlider.Value, ShakeUpSlider.Value,
         ShakeDownSlider.Value, ShakeSpeedSlider.Value);

    // ---- hotkey ----

    private bool _rebinding;

    /// <summary>True while the building rate is driving the clicker.</summary>
    private bool _building;

    /// <summary>What the shake toggle was before the combo key turned it on.</summary>
    private bool? _shakeBeforeCombo;

    private void WireHotkey()
    {
        _hotkeys.Pressed += code => Dispatcher.UIThread.Post(() => Fire(code));
        _hotkeys.Released += code => Dispatcher.UIThread.Post(() => Lifted(code));

        Bind(HotkeyButton, "clicker", (code, name) =>
        {
            _settings.HotkeyCode = code;
            _settings.HotkeyName = name;
        });

        Bind(ComboHotkeyButton, "clicker + shake", (code, name) =>
        {
            _settings.ComboCode = code;
            _settings.ComboName = name;
        });

        Bind(BuildHotkeyButton, "building", (code, name) =>
        {
            _settings.BuildCode = code;
            _settings.BuildName = name;
        });

        Bind(RecordHotkeyButton, "record", (code, name) =>
        {
            _settings.RecordCode = code;
            _settings.RecordName = name;
        });

        Bind(ReplayHotkeyButton, "save replay", (code, name) =>
        {
            _settings.ReplayCode = code;
            _settings.ReplayName = name;
        });

        BuildRateText.Text =
            $"Fixed {Clicker.BuildCps:0} CPS at {Clicker.BuildDuty * 100:0}% — Ignores the sliders";
    }

    /// <summary>
    /// Runs whichever action the pressed key is bound to.
    /// </summary>
    /// <remarks>
    /// Checked in the order the cards appear, so binding one key to two actions
    /// does the top one rather than both. Nothing prevents that binding — it is
    /// the user's key and they may have meant it — but doing two things at once
    /// would not be what anyone meant.
    /// </remarks>
    private void Fire(int code)
    {
        if (code == 0) return;

        if (code == _settings.HotkeyCode)
        {
            // Hold mode latches nothing: the press starts, the release stops,
            // and a press while already running is a repeat rather than a stop.
            if (HoldModeButton.IsChecked == true)
            {
                if (!_clicker.IsRunning) Toggle();
            }
            else
            {
                Toggle();
            }
        }
        else if (code == _settings.ComboCode) ToggleCombo();
        else if (code == _settings.BuildCode) ToggleBuilding();
        else if (code == _settings.RecordCode) _ = ToggleRecording();
        else if (code == _settings.ReplayCode) _ = SaveReplay();
    }

    /// <summary>Stops the clicker when the held key comes back up.</summary>
    /// <remarks>
    /// Only the plain keybind holds. The combo and building keys stay toggles
    /// whatever the mode says — building especially, which is used while both
    /// hands are busy doing something else.
    /// </remarks>
    private void Lifted(int code)
    {
        if (code == 0 || code != _settings.HotkeyCode) return;
        if (HoldModeButton.IsChecked != true) return;

        if (_clicker.IsRunning) Toggle();
    }

    /// <summary>
    /// Puts a button into rebind mode and stores whatever key is pressed next.
    /// </summary>
    /// <remarks>
    /// The watcher stops reporting presses while a capture is pending, so the
    /// key being chosen cannot also fire the action it is being bound to. It
    /// also holds that key as already-down afterwards, which is the other half
    /// of the same problem: without it, the very press that picked the key would
    /// register as a fresh press the instant the binding took effect and start
    /// the clicker before the user let go.
    /// </remarks>
    private void Bind(Button button, string action, Action<int, string> store)
    {
        button.Click += (_, _) =>
        {
            if (_rebinding) return;

            _rebinding = true;

            object? previous = button.Content;
            button.Content = "Press a key…";

            HotkeyNoticeText.IsVisible = false;

            _hotkeys.CaptureNext((code, name) => Dispatcher.UIThread.Post(() =>
            {
                _rebinding = false;

                // One key, one action. Bound twice, only the first would ever
                // run — which reads as a hotkey that quietly stopped working
                // rather than as a clash, so it is refused by name instead.
                string? taken = Bindings()
                    .Where(b => b.Code == code && b.Action != action)
                    .Select(b => b.Action)
                    .FirstOrDefault();

                if (taken != null)
                {
                    button.Content = previous;

                    HotkeyNoticeText.Text = $"{name} is already the {taken} key. Pick another.";
                    HotkeyNoticeText.IsVisible = true;
                    return;
                }

                store(code, name);
                button.Content = name;

                _settings.Save();
                ArmHotkeys();
            }));
        };
    }

    /// <summary>
    /// Every binding, with the action it belongs to.
    /// </summary>
    /// <remarks>
    /// One list, read by both the clash check and the summary on the Settings
    /// page. Kept apart they would disagree the first time a sixth action was
    /// added and only one of them updated.
    /// </remarks>
    private (string Action, int Code, string Name)[] Bindings() => new[]
    {
        ("clicker", _settings.HotkeyCode, _settings.HotkeyName),
        ("clicker + shake", _settings.ComboCode, _settings.ComboName),
        ("building", _settings.BuildCode, _settings.BuildName),
        ("record", _settings.RecordCode, _settings.RecordName),
        ("save replay", _settings.ReplayCode, _settings.ReplayName)
    };

    /// <summary>
    /// Tells the watcher which keys to look for, or none at all.
    /// </summary>
    /// <remarks>
    /// The master switch clears the watch list rather than unbinding anything,
    /// so the bindings are still on their buttons when it comes back on. The
    /// same call rebuilds the summary on the Settings page, which is the only
    /// place all five are listed together.
    /// </remarks>
    private void ArmHotkeys()
    {
        bool on = HotkeysEnabled.IsChecked == true;

        _hotkeys.Watch(on
            ? new[]
            {
                _settings.HotkeyCode, _settings.ComboCode, _settings.BuildCode,
                _settings.RecordCode, _settings.ReplayCode
            }
            : Array.Empty<int>());

        RefreshHotkeySummary();
        RefreshStatus();

        if (_loading) return;

        _settings.HotkeysOn = on;
        _settings.Save();
    }

    /// <summary>Starts the clicker and the shake together, and stops both.</summary>
    private void ToggleCombo()
    {
        if (_clicker.IsRunning)
        {
            Toggle();

            // Put the toggle back where it was, so the plain keybind does not
            // silently inherit shake from a combo run earlier.
            if (_shakeBeforeCombo is bool was)
            {
                ShakeToggle.IsChecked = was;
                _shakeBeforeCombo = null;
            }
        }
        else
        {
            _shakeBeforeCombo = ShakeToggle.IsChecked == true;
            ShakeToggle.IsChecked = true;

            Toggle();
        }
    }

    /// <summary>
    /// Clicks at the fixed building rate for as long as it is on.
    /// </summary>
    /// <remarks>
    /// The rate is not adjustable, and that is the feature. Building wants a
    /// specific slow, long press that places blocks reliably; a slider next to
    /// it would only be a way to break it.
    /// </remarks>
    private void ToggleBuilding()
    {
        if (_clicker.IsRunning)
        {
            _building = false;
            _clicker.Stop();
            FlushHistory();
        }
        else
        {
            if (!_engine.IsAvailable)
            {
                StatusText.Text = "BLOCKED";
                RefreshPermissionBanner();
                NavClicker.IsChecked = true;
                return;
            }

            _building = true;
            Publish();
            _clicker.Start();
        }

        RefreshShake();
        RefreshStatus();
    }

    // ---- settings ----

    private void ApplySettings()
    {
        _loading = true;

        // Clamped to the minimums the window declares. A stored size from a
        // larger monitor is only a problem if it is honoured blindly, and a
        // window that opens bigger than the screen cannot be resized back.
        if (_settings.WindowWidth is double width)
            Width = Math.Clamp(width, MinWidth, 4000);

        if (_settings.WindowHeight is double height)
            Height = Math.Clamp(height, MinHeight, 3000);

        CpsSlider.Value = Math.Clamp(_settings.Cps, CpsSlider.Minimum, CpsSlider.Maximum);
        DutySlider.Value = Math.Clamp(_settings.Cdc, DutySlider.Minimum, DutySlider.Maximum);

        HitFixToggle.IsChecked = _settings.HitFix;
        SpinToggle.IsChecked = _settings.UltraAccuracy;

        ClickButton restored = ClickButtons.Parse(_settings.ClickButton);

        ButtonLeft.IsChecked = restored == ClickButton.Left;
        ButtonRight.IsChecked = restored == ClickButton.Right;
        ButtonMiddle.IsChecked = restored == ClickButton.Middle;

        ShakeLeftSlider.Value = _settings.ShakeLeft;
        ShakeRightSlider.Value = _settings.ShakeRight;
        ShakeUpSlider.Value = _settings.ShakeUp;
        ShakeDownSlider.Value = _settings.ShakeDown;
        ShakeSpeedSlider.Value = _settings.ShakeSpeed;
        ShakeToggle.IsChecked = _settings.Shake;

        HotkeysEnabled.IsChecked = _settings.HotkeysOn;
        MenuBarEnabled.IsChecked = _settings.MenuBar;
        AutoCheckUpdates.IsChecked = _settings.AutoCheckUpdates;

        HotkeyButton.Content = _settings.HotkeyName;
        ComboHotkeyButton.Content = _settings.ComboName;
        BuildHotkeyButton.Content = _settings.BuildName;

        ArmHotkeys();

        _fps = _settings.RecordFps;
        _replaySeconds = _settings.ReplaySeconds;

        Replay15.IsChecked = _replaySeconds == 15;
        Replay30.IsChecked = _replaySeconds == 30;
        Replay60.IsChecked = _replaySeconds == 60;

        RecordHotkeyButton.Content = _settings.RecordName;
        ReplayHotkeyButton.Content = _settings.ReplayName;

        HoldModeButton.IsChecked = _settings.HoldMode;
        ToggleModeButton.IsChecked = !_settings.HoldMode;

        // Whatever was loaded is the saved set by definition, so Restore has
        // something to go back to from the first launch.
        _savedShake = CurrentShake();
        RestoreShakeButton.IsEnabled = true;

        Fps30.IsChecked = _fps == 30;
        Fps60.IsChecked = _fps == 60;
        Fps120.IsChecked = _fps == 120;
        Fps144.IsChecked = _fps == 144;

        // A hand-edited rate that matches no button still has to be honoured,
        // or the app quietly records at something the file does not say.
        if (_fps is not (30 or 60 or 120 or 144)) Fps60.IsChecked = false;

        ThemeDark.IsChecked = _settings.Dark;
        ThemeLight.IsChecked = !_settings.Dark;
        SetMode(_settings.Dark);

        OpacitySlider.Value = Math.Clamp(_settings.Opacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
        SetOpacity(OpacitySlider.Value);

        WallpaperDimmingSlider.Value = _settings.WallpaperDimming;

        try
        {
            SetAccent(Color.Parse(_settings.AccentColor));
        }
        catch
        {
            // A hand-edited colour that will not parse falls back to the
            // default rather than taking the window down.
        }

        RefreshReadouts();

        _loading = false;

        ApplyWallpaper();
    }

    private void Persist()
    {
        if (_loading) return;

        _settings.Cps = CpsSlider.Value;
        _settings.Cdc = DutySlider.Value;
        _settings.HitFix = HitFixToggle.IsChecked == true;
        _settings.UltraAccuracy = SpinToggle.IsChecked == true;
        _settings.ClickButton = SelectedButton.ToString();

        _settings.Shake = ShakeToggle.IsChecked == true;
        _settings.ShakeLeft = ShakeLeftSlider.Value;
        _settings.ShakeRight = ShakeRightSlider.Value;
        _settings.ShakeUp = ShakeUpSlider.Value;
        _settings.ShakeDown = ShakeDownSlider.Value;
        _settings.ShakeSpeed = ShakeSpeedSlider.Value;

        // Only a normal window has a size worth keeping. Saving while minimised
        // or maximised stores the wrong one and the next launch opens at it.
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }

        _settings.Save();
    }

    // ---- presets ----

    private void WirePresets()
    {
        SavePresetButton.Click += (_, _) => SaveTypedPreset();
        RestorePresetsButton.Click += (_, _) => RestoreDefaultPresets();

        FromSlidersButton.Click += (_, _) =>
        {
            PresetCpsBox.Text = CpsSlider.Value.ToString("0.00", CultureInfo.CurrentCulture);
            PresetCdcBox.Text = DutySlider.Value.ToString("0.00", CultureInfo.CurrentCulture);
            PresetNameBox.Focus();
        };

        BuildPresets();
    }

    private void BuildPresets()
    {
        PresetList.Items.Clear();
        _presetCards.Clear();

        foreach (ClickPreset preset in _presets) PresetList.Items.Add(PresetCard(preset));

        NoPresetsText.IsVisible = _presets.Count == 0;

        MarkApplied();
    }

    /// <summary>
    /// Lights up whichever card matches the sliders.
    /// </summary>
    /// <remarks>
    /// Driven off the slider values rather than remembering what was last
    /// clicked, so the highlight is telling the truth rather than telling a
    /// history. Moving a slider by hand, applying from the Clicker page, or
    /// loading last session's settings all light the matching card and unlight
    /// the rest, with no extra bookkeeping to get out of step.
    ///
    /// More than one card can match — nothing stops two presets holding the same
    /// pair — and both light up, which is accurate.
    /// </remarks>
    private void MarkApplied()
    {
        if (_presetCards.Count == 0) return;

        var accent = this.FindResource("Accent") as IBrush;
        var sunken = this.FindResource("Sunken") as IBrush;
        var panel = this.FindResource("Panel") as IBrush;

        foreach ((ClickPreset preset, Border card, Border rail) in _presetCards)
        {
            // Both sliders, not just the rate. Two presets can share a CPS and
            // differ entirely in how long the button is held.
            bool applied =
                Math.Abs(preset.Cps - CpsSlider.Value) < Tolerance
                && Math.Abs(preset.Cdc - DutySlider.Value) < Tolerance;

            card.BorderBrush = applied ? accent : Brushes.Transparent;
            card.Background = applied ? panel : sunken;
            rail.IsVisible = applied;
        }
    }

    /// <summary>Half of the smallest step the two-decimal readouts can show.</summary>
    private const double Tolerance = 0.005;

    /// <summary>
    /// One preset card: the rates, what mode it applies, and a bar so speed is
    /// legible before the number is read.
    /// </summary>
    /// <remarks>
    /// The card applies on click and the two small buttons do not, which is only
    /// safe because pointer events bubble — both handlers mark theirs handled,
    /// or deleting a preset would also apply it on the way past.
    /// </remarks>
    private Control PresetCard(ClickPreset preset)
    {
        var name = new TextBlock
        {
            Text = preset.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Button edit = SmallButton("✎", "Edit this preset");
        Button remove = SmallButton("✕", "Delete this preset");

        edit.Click += (_, e) =>
        {
            e.Handled = true;

            // The pencil toggles. Clicking the one already loaded is how you
            // back out of an edit you did not mean to start — otherwise the row
            // stays filled and the next Save quietly overwrites that preset.
            if (ReferenceEquals(_editing, preset))
            {
                ClearCustomRow();
                return;
            }

            _editing = preset;

            PresetNameBox.Text = preset.Name;
            PresetCpsBox.Text = preset.CpsText;
            PresetCdcBox.Text = preset.CdcText;

            // Saving under the same name replaces it, so editing is just
            // filling the row back in and pressing Save again.
            CustomHint.Text = $"Editing “{preset.Name}” — saving replaces it";
            PresetError.IsVisible = false;
            PresetNameBox.Focus();
        };

        remove.Click += (_, e) =>
        {
            e.Handled = true;

            // Deleting what is loaded in the row leaves it describing something
            // that no longer exists, and Save would put it straight back.
            if (ReferenceEquals(_editing, preset)) ClearCustomRow();

            _presets.Remove(preset);
            PresetStore.Save(_presets);
            BuildPresets();
        };

        var header = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(edit);
        buttons.Children.Add(remove);
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        header.Children.Add(name);

        var rates = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = this.FindResource("Accent") as IBrush,
            Text = $"{preset.CpsText} CPS  ·  {preset.CdcText}% CDC"
        };

        var mode = new TextBlock
        {
            Text = preset.ModeText,
            FontSize = 10,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = this.FindResource("TextMuted") as IBrush
        };

        var fill = new Border
        {
            Height = 3,
            Width = preset.BarWidth,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = this.FindResource("Accent") as IBrush
        };

        var track = new Border
        {
            Height = 3,
            Margin = new Thickness(0, 8, 0, 0),
            CornerRadius = new CornerRadius(2),
            Background = this.FindResource("Hairline") as IBrush,
            Child = fill
        };

        // The rail down the left edge. Hidden until the preset is the one in
        // force — a border alone is easy to miss across eleven cards, and this
        // is the same mark the sidebar uses for the current page.
        var rail = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = this.FindResource("Accent") as IBrush,
            IsVisible = false
        };

        var body = new StackPanel { Children = { header, rates, mode, track } };

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(rail, 0);
        Grid.SetColumn(body, 1);
        layout.Children.Add(rail);
        layout.Children.Add(body);

        var card = new Border
        {
            Background = this.FindResource("Sunken") as IBrush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 10, 10),
            Width = 176,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = layout
        };

        card.PointerPressed += (_, e) =>
        {
            if (e.Handled) return;

            Apply(preset);
        };

        _presetCards.Add((preset, card, rail));

        return card;
    }

    private Button SmallButton(string glyph, string tip) => new()
    {
        Content = glyph,
        FontSize = 11,
        Width = 22,
        Height = 22,
        Padding = new Thickness(0),
        Margin = new Thickness(4, 0, 0, 0),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Foreground = this.FindResource("TextMuted") as IBrush,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        [ToolTip.TipProperty] = tip
    };

    /// <summary>
    /// Puts a preset's rates on the sliders, and stays where it is.
    /// </summary>
    /// <remarks>
    /// Deliberately does not navigate. Jumping to the Clicker page after every
    /// apply makes trying two presets against each other a round trip, and the
    /// card lighting up already says the apply landed.
    /// </remarks>
    private void Apply(ClickPreset preset)
    {
        CpsSlider.Value = Math.Clamp(preset.Cps, CpsSlider.Minimum, CpsSlider.Maximum);
        DutySlider.Value = Math.Clamp(preset.Cdc, DutySlider.Minimum, DutySlider.Maximum);
    }

    /// <summary>
    /// Saves what is typed in the custom row, or says why it will not.
    /// </summary>
    /// <remarks>
    /// Refuses rather than coerces. A blank CPS read as zero would save a preset
    /// that stops the clicker, and a name of spaces would produce a card nobody
    /// can identify or delete.
    /// </remarks>
    private void SaveTypedPreset()
    {
        string name = PresetNameBox.Text?.Trim() ?? "";

        if (name.Length == 0)
        {
            ShowPresetError("Give it a name.");
            return;
        }

        double? cps = PresetStore.ParseRate(PresetCpsBox.Text, CpsSlider.Maximum);
        double? cdc = PresetStore.ParseRate(PresetCdcBox.Text, DutySlider.Maximum);

        if (cps == null)
        {
            ShowPresetError($"CPS has to be a number between 0 and {CpsSlider.Maximum:0}.");
            return;
        }

        if (cdc == null)
        {
            ShowPresetError("CDC has to be a number between 0 and 100.");
            return;
        }

        PresetStore.Upsert(_presets, new ClickPreset(name, cps.Value, cdc.Value));
        PresetStore.Save(_presets);

        ClearCustomRow();
        BuildPresets();
    }

    /// <summary>Empties the custom row and forgets whatever it was editing.</summary>
    private void ClearCustomRow()
    {
        _editing = null;

        PresetNameBox.Text = "";
        PresetCpsBox.Text = "";
        PresetCdcBox.Text = "";

        PresetError.IsVisible = false;
        CustomHint.Text = "Name it, set CPS and CDC, and it is saved between sessions";
    }

    private void ShowPresetError(string message)
    {
        PresetError.Text = message;
        PresetError.IsVisible = true;
    }

    /// <summary>
    /// Adds back any shipped preset that is missing, without touching the rest.
    /// </summary>
    /// <remarks>
    /// Add rather than replace. Someone who has built up their own list and
    /// wants Sky back should not lose the eleven they made getting there.
    /// </remarks>
    private void RestoreDefaultPresets()
    {
        foreach (ClickPreset preset in PresetStore.Defaults())
        {
            bool present = _presets.Any(p =>
                string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));

            if (!present) _presets.Add(preset);
        }

        PresetStore.Save(_presets);
        BuildPresets();
    }

    // ---- recorder ----

    private void WireRecorder()
    {
        RecordButton.Click += async (_, _) => await ToggleRecording();
        RescanButton.Click += async (_, _) => await RescanScreens();
        OpenClipsButton.Click += (_, _) => Reveal(ClipFolder);
        RevealClipButton.Click += (_, _) => RevealClip();

        Rate(Fps30, 30);
        Rate(Fps60, 60);
        Rate(Fps120, 120);
        Rate(Fps144, 144);

        Length(Replay15, 15);
        Length(Replay30, 30);
        Length(Replay60, 60);

        ReplayEnabled.IsCheckedChanged += (_, _) => RefreshReplay();

        RecheckFfmpegButton.Click += async (_, _) => await RescanScreens();

        InstallFfmpegButton.Click += (_, _) => InstallFfmpeg();

        CopyFfmpegButton.Click += async (_, _) =>
        {
            if (Clipboard != null) await Clipboard.SetTextAsync(Ffmpeg.InstallHint);

            CopyFfmpegButton.Content = "Copied";
        };
        SaveReplayButton.Click += async (_, _) => await SaveReplay();

        ChooseClipButton.Click += async (_, _) => await ChooseClip();
        UploadClipButton.Click += async (_, _) => await UploadClip();
        CopyLinkButton.Click += async (_, _) => await CopyLink();


        void Rate(RadioButton button, int fps) =>
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked != true) return;

                _fps = fps;

                if (_loading) return;

                _settings.RecordFps = fps;
                _settings.Save();
            };

        void Length(RadioButton button, int seconds) =>
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked != true) return;

                _replaySeconds = seconds;

                if (_loading) return;

                _settings.ReplaySeconds = seconds;
                _settings.Save();

                // The buffer holds a fixed span, so a new length is a new
                // buffer. Restarted rather than resized.
                if (_replay.IsRunning)
                {
                    _replay.Stop();
                    RefreshReplay();
                }
            };
    }

    /// <summary>
    /// Asks ffmpeg what it can capture from, and says so plainly when the answer
    /// is nothing.
    /// </summary>
    /// <remarks>
    /// Rescan is a button as well as something that happens at startup, because
    /// plugging in a monitor changes the answer and because the most likely
    /// reason the list is empty — ffmpeg not installed — is fixable without
    /// closing the app.
    ///
    /// Off the UI thread, and that is not a nicety. Both halves of this start a
    /// process: listing devices, and encoding one frame to find out whether the
    /// hardware encoder really initialises. Run inline they would hold the
    /// window shut for as long as ffmpeg took to answer — which on a Mac
    /// waiting on a permission prompt is not a bounded amount of time.
    /// </remarks>
    private async Task RescanScreens()
    {
        DisplayPanel.Children.Clear();
        _screens.Clear();

        string? ffmpeg = Ffmpeg.Find();

        if (ffmpeg == null)
        {
            ScreenNote.Text = $"ffmpeg was not found. Install it with:  {Ffmpeg.InstallHint}";
            BackendDetail.Text = "No encoder — ffmpeg is missing.";

            // Left clickable on purpose. Greying these out meant the tester met
            // a checkbox that would not tick and said nothing about why — the
            // reason was on a different card, and a control that refuses to be
            // touched teaches nothing. Pressed now, each one fails with the
            // command that fixes it.
            ReplayNoteText.Text = $"Needs ffmpeg, which is not installed. {Ffmpeg.InstallHint}";
            ReplayNoteText.IsVisible = true;

            FfmpegCommandText.Text = Ffmpeg.InstallHint;
            FfmpegBanner.IsVisible = true;
            ShowInstallStep();

            return;
        }

        ReplayNoteText.IsVisible = false;
        FfmpegBanner.IsVisible = false;
        ScreenNote.Text = "Looking…";

        (List<CaptureDevice> found, string encoder) = await Task.Run(() =>
            (CaptureBackend.Screens(ffmpeg), CaptureBackend.EncoderArgs(ffmpeg)));

        _screens.AddRange(found);

        foreach (CaptureDevice screen in _screens) DisplayPanel.Children.Add(ScreenButton(screen));

        if (_screens.Count > 0)
        {
            // Falls back to the first rather than leaving nothing chosen, so
            // Record always has something to capture.
            CaptureDevice wanted = _screens.FirstOrDefault(s => s.Index == _settings.RecordScreen)
                                   ?? _screens[0];

            _screen = wanted;
            ((RadioButton)DisplayPanel.Children[_screens.IndexOf(wanted)]).IsChecked = true;

            ScreenNote.Text = "";
        }
        else
        {
            _screen = null;

            ScreenNote.Text = OperatingSystem.IsMacOS()
                ? "No screens listed. macOS hides them until Screen Recording permission is granted — "
                  + "System Settings > Privacy & Security > Screen Recording."
                : "Screen enumeration is a macOS path. On Windows this records the whole desktop.";
        }

        BackendDetail.Text =
            $"{(OperatingSystem.IsMacOS() ? "avfoundation" : "gdigrab")}"
            + $"  ·  {CaptureBackend.EncoderName ?? encoder}  ·  {ffmpeg}";
    }

    /// <summary>
    /// Finds the screens, then starts the buffer if it was left on.
    /// </summary>
    /// <remarks>
    /// Ordered, not merged. The buffer captures whichever screen is chosen, and
    /// starting it before the scan finishes would buffer a guess.
    /// </remarks>
    private async Task StartCapture()
    {
        await RescanScreens();

        RefreshRecording();

        bool was = _loading;
        _loading = true;
        ReplayEnabled.IsChecked = _settings.ReplayEnabled;
        _loading = was;

        RefreshReplay();
    }

    /// <summary>
    /// Says what the install button is actually going to do.
    /// </summary>
    /// <remarks>
    /// Which depends on whether Homebrew is there. Offering "Install it for me"
    /// on a machine without brew would open a Terminal that immediately says
    /// command not found, which is a worse answer than the one the button
    /// promised.
    /// </remarks>
    private void ShowInstallStep()
    {
        bool brew = Ffmpeg.Homebrew() != null;

        InstallFfmpegButton.Content = brew ? "Install it for me" : "Get Homebrew first";

        FfmpegStepText.Text = brew
            ? "Opens Terminal and runs the command there, so you can watch it and stop it. "
              + "It takes a few minutes. Come back and press Check again when it finishes."
            : "ffmpeg comes from Homebrew, which is not installed either. This opens brew.sh — "
              + "install that first, then come back and this button will finish the job.";
    }

    /// <summary>
    /// Runs the install, or sends the user to get the thing that runs it.
    /// </summary>
    /// <remarks>
    /// Never silently. The app does not install software the user did not press
    /// a button for and cannot watch — and it does not pipe a script from the
    /// internet into a shell on their behalf, which is what installing Homebrew
    /// itself would mean.
    /// </remarks>
    private void InstallFfmpeg()
    {
        if (Ffmpeg.Homebrew() == null)
        {
            Open("https://brew.sh");
            return;
        }

        FfmpegStepText.Text = Ffmpeg.OpenInstaller()
            ? "Terminal is running the install. Press Check again once it finishes."
            : "Terminal would not open. Copy the command and run it yourself.";
    }

    /// <summary>Opens a link in the default browser.</summary>
    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // A link that will not open is not worth a crash.
        }
    }

    private RadioButton ScreenButton(CaptureDevice screen)
    {
        var button = new RadioButton
        {
            Content = screen.Name,
            GroupName = "Screens",
            Classes = { "segment" }
        };

        button.IsCheckedChanged += (_, _) =>
        {
            if (button.IsChecked != true) return;

            _screen = screen;

            if (_loading) return;

            _settings.RecordScreen = screen.Index;
            _settings.Save();
        };

        return button;
    }

    /// <summary>
    /// Whether macOS will actually let a capture happen, said before trying.
    /// </summary>
    /// <remarks>
    /// Checked here rather than left to ffmpeg. Without Screen Recording
    /// permission avfoundation does not refuse — it hands back an I/O error or,
    /// worse, black frames, and the message that reaches the page is ffmpeg
    /// complaining about a device rather than macOS explaining a permission.
    ///
    /// Accessibility and Screen Recording are separate grants and people
    /// reasonably assume the first covers the second, because both live on the
    /// same page of System Settings.
    /// </remarks>
    private void WarnIfCaptureBlocked()
    {
        if (MacPermissions.ScreenRecording() != Permission.Denied) return;

        RecordError.Text =
            "Screen Recording permission looks missing, so the clip will probably come out black — "
            + "macOS does not refuse the capture, it just hands over empty frames. "
            + MacPermissions.Where("Screen Recording")
            + "\n\nIt is a separate grant from Accessibility; one does not imply the other. "
            + "If it is already on and clips are still black, add the ffmpeg binary itself: "
            + "press + in that list, then Command-Shift-G and paste "
            + (Ffmpeg.Find() ?? "/opt/homebrew/bin/ffmpeg")
            + " — the capture is done by ffmpeg, not by Jinxy, and an unsigned app cannot always "
            + "lend its permission to a program it launches.";

        RecordError.IsVisible = true;
    }

    private async Task ToggleRecording()
    {
        RecordError.IsVisible = false;

        if (!_recorder.IsRecording) WarnIfCaptureBlocked();

        RecordButton.IsEnabled = false;

        try
        {
            if (_recorder.IsRecording)
            {
                string? clip = await _recorder.StopAsync();

                if (clip == null)
                {
                    RecordError.Text = "The clip was empty and has been discarded.";
                    RecordError.IsVisible = true;
                }
                else
                {
                    // Offered straight to the uploader, because the clip someone
                    // wants to share is almost always the one just recorded.
                    Choose(clip);
                    Announce(clip);
                    RecordStatus.Text = "";

                    Notify.Send("Recording saved", System.IO.Path.GetFileName(clip));
                }
            }
            else
            {
                await _recorder.StartAsync(ClipFolder, _fps, _screen);
                RecordStatus.Text = "";
            }
        }
        catch (Exception error)
        {
            // The reason, not the fact that there is one. ffmpeg always says
            // what went wrong; the only way to lose it is to throw it away.
            RecordError.Text = error.Message;
            RecordError.IsVisible = true;
        }
        finally
        {
            RecordButton.IsEnabled = true;
            RefreshRecording();
        }
    }

    private void RefreshRecording()
    {
        bool recording = _recorder.IsRecording;

        RecordButton.Content = recording ? "Stop recording" : "Start recording";

        TimeSpan elapsed = recording ? DateTime.UtcNow - _recorder.StartedUtc : TimeSpan.Zero;

        RecordElapsedText.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

    /// <summary>Opens a folder in the system file browser, creating it first.</summary>
    private static void Reveal(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);

            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch
        {
            // A folder that will not open is not worth a crash; the path is on
            // screen either way.
        }
    }

    // ---- instant replay ----

    /// <summary>
    /// Starts or stops the rolling buffer to match the toggle.
    /// </summary>
    /// <remarks>
    /// This is the expensive one. The recorder runs while you ask it to; the
    /// buffer runs all session, re-encoding the screen the entire time. It stays
    /// off unless asked for.
    /// </remarks>
    private void RefreshReplay()
    {
        bool wanted = ReplayEnabled.IsChecked == true;

        // Same permission, same silent failure — a buffer full of black frames
        // is worse than one that never started, because it looks like it works
        // until someone saves a clip.
        if (wanted && !_replay.IsRunning && MacPermissions.ScreenRecording() == Permission.Denied)
        {
            ReplayStatusText.Text =
                "Screen Recording permission is not granted. " + MacPermissions.Where("Screen Recording");

            _loading = true;
            ReplayEnabled.IsChecked = false;
            _loading = false;

            return;
        }

        try
        {
            if (wanted && !_replay.IsRunning)
            {
                _replay.Start(_replaySeconds, _fps, _screen);
                ReplayStatusText.Text = $"Buffering the last {_replaySeconds} seconds.";
            }
            else if (!wanted && _replay.IsRunning)
            {
                _replay.Stop();
                ReplayStatusText.Text = "";
            }
        }
        catch (Exception error)
        {
            ReplayStatusText.Text = error.Message;
            ReplayEnabled.IsChecked = false;
        }

        if (_loading) return;

        _settings.ReplayEnabled = ReplayEnabled.IsChecked == true;
        _settings.Save();
    }

    private async Task SaveReplay()
    {
        if (!_replay.IsRunning)
        {
            ReplayStatusText.Text = "Instant replay is off, so there is nothing buffered to save.";
            return;
        }

        SaveReplayButton.IsEnabled = false;
        ReplayStatusText.Text = "Saving…";

        try
        {
            string? clip = await _replay.SaveLastAsync(_replaySeconds, ClipFolder);

            if (clip == null)
            {
                ReplayStatusText.Text = "The buffer held nothing usable yet — give it a few seconds.";
            }
            else
            {
                Choose(clip);
                Announce(clip);
                ReplayStatusText.Text = "";

                // The one that most needs saying out loud: the key was pressed
                // mid-game with this window behind Roblox.
                Notify.Send($"Last {_replaySeconds}s saved", System.IO.Path.GetFileName(clip));
            }
        }
        catch (Exception error)
        {
            ReplayStatusText.Text = error.Message;
        }
        finally
        {
            SaveReplayButton.IsEnabled = true;
        }
    }

    // ---- sharing ----

    private async Task ChooseClip()
    {
        IReadOnlyList<IStorageFile> picked = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Choose a clip",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Video")
                    {
                        Patterns = new[] { "*.mp4", "*.mov", "*.mkv", "*.webm" }
                    }
                }
            });

        if (picked.Count == 0) return;

        string? path = picked[0].TryGetLocalPath();

        if (path != null) Choose(path);
    }

    /// <summary>
    /// Announces where a clip landed, with a way to go there.
    /// </summary>
    /// <remarks>
    /// The full path, not the filename. A recorder that reports
    /// "Saved clip-2026-08-23-191327.mp4" has told the user nothing they can
    /// act on — the folder is the part they are missing, and on macOS
    /// ~/Movies is not somewhere people browse by habit.
    /// </remarks>
    private void Announce(string clip)
    {
        SavedNameText.Text = "Saved " + System.IO.Path.GetFileName(clip);
        SavedPathText.Text = clip;
        SavedBox.IsVisible = true;

        RevealClipButton.IsEnabled = true;
        _lastClip = clip;
    }

    /// <summary>Opens Finder with the clip itself selected, not just its folder.</summary>
    private void RevealClip()
    {
        if (_lastClip == null) return;

        try
        {
            // open -R selects the file in its folder, which is the difference
            // between "here is the folder" and "here is your clip".
            var info = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/open" : "explorer.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (OperatingSystem.IsMacOS())
            {
                info.ArgumentList.Add("-R");
                info.ArgumentList.Add(_lastClip);
            }
            else
            {
                info.ArgumentList.Add("/select," + _lastClip);
            }

            Process.Start(info);
        }
        catch
        {
            // The path is on screen and selectable either way.
        }
    }

    /// <summary>Points the uploader at a file, without uploading it.</summary>
    /// <remarks>
    /// Choosing and uploading are deliberately two actions. Uploading publishes
    /// the clip permanently and cannot be undone, so it never happens as a side
    /// effect of recording one.
    /// </remarks>
    private void Choose(string path)
    {
        _clip = path;

        var file = new FileInfo(path);

        ChosenClipText.Text = file.Exists
            ? $"{file.Name}  ·  {file.Length / 1024.0 / 1024.0:0.0} MB"
            : path;

        UploadClipButton.IsEnabled = file.Exists;
    }

    private async Task UploadClip()
    {
        if (_clip == null) return;

        UploadClipButton.IsEnabled = false;
        UploadStatusText.Text = "Uploading…";

        UploadResult result = await ClipUploader.UploadAsync(_clip, CancellationToken.None);

        UploadStatusText.Text = result.Message;
        UploadClipButton.IsEnabled = true;

        if (!result.Success || result.Url == null) return;

        ClipUrlBox.Text = result.Url;
        CopyLinkButton.IsEnabled = true;

        await CopyLink();
        UploadStatusText.Text = "Uploaded, and the link is on your clipboard.";

        Notify.Send("Clip uploaded", "The link is on your clipboard.");
    }

    private async Task CopyLink()
    {
        string url = ClipUrlBox.Text ?? "";

        if (url.Length == 0 || Clipboard == null) return;

        await Clipboard.SetTextAsync(url);
    }

    // ---- history ----

    private void WireHistory()
    {
        ResetHistoryButton.Click += (_, _) =>
        {
            _history.Reset();
            _history.Save();
            _historyDirty = false;

            RefreshHistory();
        };
    }

    /// <summary>
    /// Folds the last second of clicking into the running totals.
    /// </summary>
    /// <remarks>
    /// Driven off the same delta the measured-rate readout uses, so the two can
    /// never disagree. Only counts while the engine is actually delivering
    /// clicks — the app being open is not clicking.
    /// </remarks>
    private void RecordActivity(double seconds, long clicks)
    {
        if (!_clicker.IsRunning || seconds <= 0) return;

        _history.Add(DateTime.Now, seconds, clicks);
        _historyDirty = true;
    }

    /// <summary>Written on stop rather than every second, to spare the disk.</summary>
    private void FlushHistory()
    {
        if (!_historyDirty) return;

        _history.Save();
        _historyDirty = false;
    }

    private void RefreshHistory()
    {
        TotalTimeText.Text = ClickHistory.FormatDuration(TimeSpan.FromSeconds(_history.TotalSeconds));
        TotalClicksText.Text = _history.TotalClicks.ToString("N0", CultureInfo.CurrentCulture);
        AverageRateText.Text = _history.AverageRateText;
        DaysText.Text = _history.DaysRecorded.ToString();

        HistoryList.Items.Clear();

        List<HistoryDay> days = _history.RecentDays();
        NoHistoryText.IsVisible = days.Count == 0;

        foreach (HistoryDay day in days) HistoryList.Items.Add(HistoryRow(day));
    }

    private Control HistoryRow(HistoryDay day)
    {
        var date = new TextBlock
        {
            Text = day.DateText,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var detail = new TextBlock
        {
            Text = $"{day.DurationText}   ·   {day.ClicksText} clicks   {day.RateText}",
            FontSize = 11,
            Foreground = this.FindResource("TextMuted") as IBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var row = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(detail, Dock.Right);
        row.Children.Add(date);
        row.Children.Add(detail);

        return new Border
        {
            Background = this.FindResource("Sunken") as IBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 6),
            Child = row
        };
    }

    // ---- theme ----

    private void WireTheme()
    {
        BuildSwatches();

        ThemeDark.IsCheckedChanged += (_, _) =>
        {
            if (ThemeDark.IsChecked == true) SetMode(dark: true);
        };

        ThemeLight.IsCheckedChanged += (_, _) =>
        {
            if (ThemeLight.IsChecked == true) SetMode(dark: false);
        };

        OpacitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) SetOpacity(OpacitySlider.Value);
        };

        CustomAccentButton.Click += (_, _) => UseCustomAccent();

        CustomAccentBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            UseCustomAccent();
            e.Handled = true;
        };

        ChooseWallpaperButton.Click += async (_, _) => await ChooseWallpaperAsync();

        ClearWallpaperButton.Click += (_, _) =>
        {
            Wallpaper.Clear();
            _settings.WallpaperName = "";
            ApplyWallpaper();
            Persist();
        };

        WallpaperDimmingSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;

            _settings.WallpaperDimming = Wallpaper.ClampDimming((int)WallpaperDimmingSlider.Value);
            ApplyWallpaper();

            if (!_loading) Persist();
        };
    }

    /// <summary>
    /// What the file picker offers, built from the same list the store accepts.
    /// </summary>
    /// <remarks>
    /// Lives here rather than beside <see cref="Wallpaper.Allowed"/> because
    /// Core carries no Avalonia dependency — the test project compiles those
    /// files without it. Derived from that array so a format cannot be added to
    /// one and missed in the other.
    /// </remarks>
    private static FilePickerFileType ImageFileType => new("Images")
    {
        Patterns = Wallpaper.Allowed.Select(e => "*" + e).ToArray()
    };

    private async Task ChooseWallpaperAsync()
    {
        try
        {
            IReadOnlyList<IStorageFile> picked = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Choose a background",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { ImageFileType }
                });

            string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
            if (path == null) return;

            string? stored = Wallpaper.Store(path);
            if (stored == null)
            {
                WallpaperStatusText.Text = "That file could not be read. Try a PNG or JPEG.";
                return;
            }

            _settings.WallpaperName = stored;
            ApplyWallpaper();
            Persist();
        }
        catch
        {
            // A cancelled or failed pick leaves the previous background alone.
        }
    }

    /// <summary>Paints the stored wallpaper, or nothing when there is not one.</summary>
    private void ApplyWallpaper()
    {
        string? path = Wallpaper.Resolve(_settings.WallpaperName);

        WallpaperImage.Source = null;

        // Both layers move together. A dim border left visible with no picture
        // behind it would tint the whole window for no reason.
        if (path == null)
        {
            WallpaperStatusText.Text = "No picture set.";
            WallpaperImage.IsVisible = false;
            WallpaperDim.IsVisible = false;
            return;
        }

        try
        {
            WallpaperImage.Source = new Bitmap(path);
            WallpaperImage.IsVisible = true;
            WallpaperDim.IsVisible = true;
            WallpaperDim.Opacity = Wallpaper.DimmingOpacity(_settings.WallpaperDimming);
            WallpaperStatusText.Text = $"Using {_settings.WallpaperName}.";
        }
        catch
        {
            // A file that will not decode is the same as not having one.
            WallpaperImage.IsVisible = false;
            WallpaperDim.IsVisible = false;
            WallpaperStatusText.Text = "That picture could not be decoded, so it is not being shown.";
        }
    }

    private void BuildSwatches()
    {
        foreach ((string name, string hex) in Accents.All)
        {
            var colour = Color.Parse(hex);

            var swatch = new RadioButton
            {
                GroupName = "Accent",
                Width = 34,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(colour),
                Tag = hex,
                [ToolTip.TipProperty] = name,
                Template = SwatchTemplate()
            };

            swatch.IsCheckedChanged += (_, _) =>
            {
                if (swatch.IsChecked == true) SetAccent(colour);
            };

            SwatchPanel.Children.Add(swatch);
        }
    }

    /// <summary>
    /// A swatch: the colour itself, with a ring when it is the chosen one.
    /// </summary>
    /// <remarks>
    /// The ring sits outside the fill rather than over it, because a mark drawn
    /// on top has to be either dark or light and would disappear against half
    /// the palette. Silver and Crimson both need it visible.
    /// </remarks>
    private static FuncControlTemplate SwatchTemplate() => new((_, _) =>
    {
        var ring = new Border
        {
            Name = "Ring",
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent
        };

        var fill = new Border
        {
            Name = "Fill",
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(3)
        };

        fill.Bind(Border.BackgroundProperty, new Avalonia.Data.Binding("Background")
        {
            RelativeSource = new Avalonia.Data.RelativeSource(Avalonia.Data.RelativeSourceMode.TemplatedParent)
        });

        ring.Child = fill;

        return ring;
    });

    /// <summary>
    /// Repaints every accent-coloured element, without a restart.
    /// </summary>
    /// <remarks>
    /// Moves the Color on the brush itself rather than swapping the resource.
    /// Everything already holding that brush — including controls that grabbed
    /// it once at construction — follows along, which swapping the resource
    /// would not achieve.
    ///
    /// It has to be the Application's copy, not the Window's. The brushes are
    /// declared in App.axaml, and resource lookup runs upward from the control,
    /// so a Window-level override is never consulted by them. Setting it on the
    /// Window looked correct and changed nothing.
    ///
    /// Avalonia brushes are mutable, unlike WPF's, which freezes resource
    /// Freezables and throws on exactly this assignment.
    /// </remarks>
    private void SetAccent(Color colour)
    {
        Recolour("Accent", colour);

        string hex = $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";

        AccentNameText.Text = Accents.NameOf(hex);
        MarkSwatch(hex);

        if (_loading) return;

        // Saved here rather than in Persist, which reads the sliders — the
        // accent is not one of them, and leaving it out meant the choice
        // survived until the window closed and no longer.
        _settings.AccentColor = hex;
        _settings.Save();
    }

    /// <summary>Ticks whichever swatch holds this colour, and unticks the rest.</summary>
    private void MarkSwatch(string hex)
    {
        var ring = this.FindResource("Accent") as IBrush;

        foreach (Control child in SwatchPanel.Children)
        {
            if (child is not RadioButton swatch) continue;

            bool mine = string.Equals(swatch.Tag as string, hex, StringComparison.OrdinalIgnoreCase);

            swatch.IsChecked = mine;

            if (swatch.GetTemplateChildren().FirstOrDefault(c => c.Name == "Ring") is Border border)
                border.BorderBrush = mine ? ring : Brushes.Transparent;
        }
    }

    /// <summary>
    /// Takes a hex typed by hand, for a colour not on the row.
    /// </summary>
    /// <remarks>
    /// Refuses rather than guesses. Color.Parse accepts named colours and
    /// several shorthand forms, so it is asked for the strict six-digit hex the
    /// field advertises — anything else and the message says what was wrong
    /// rather than silently painting the app an unexpected colour.
    /// </remarks>
    private void UseCustomAccent()
    {
        string typed = (CustomAccentBox.Text ?? "").Trim();

        if (!typed.StartsWith('#')) typed = "#" + typed;

        if (typed.Length != 7 || !Color.TryParse(typed, out Color colour))
        {
            AccentError.Text = "That is not a colour. Six hex digits, like #45B7FF.";
            AccentError.IsVisible = true;
            return;
        }

        AccentError.IsVisible = false;
        CustomAccentBox.Text = "";

        SetAccent(colour);
    }

    /// <summary>
    /// Swaps the whole palette, leaving the accent where it is.
    /// </summary>
    /// <remarks>
    /// The theme variant is set alongside the palette so Fluent's own internals
    /// follow — the inside of a text box, a combo box popup, a scroll bar. Those
    /// are not colours this app declares, and left on Dark they stay dark holes
    /// in a light window.
    /// </remarks>
    private void SetMode(bool dark)
    {
        foreach ((string key, string hex) in Palette.For(dark).Entries())
        {
            Recolour(key, Color.Parse(hex));
        }

        if (Application.Current is { } app)
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        if (_loading) return;

        _settings.Dark = dark;
        _settings.Save();
    }

    /// <summary>Moves one named brush to a new colour, in place.</summary>
    private static void Recolour(string key, Color colour)
    {
        if (Application.Current is not { } app) return;

        if (app.Resources.TryGetResource(key, null, out object? found) && found is SolidColorBrush brush)
            brush.Color = colour;

        // Kept in step so anything resolving the Color directly agrees with the
        // brush, rather than the two drifting apart.
        app.Resources[key + "Color"] = colour;
    }

    private void SetOpacity(double opacity)
    {
        double clamped = Math.Clamp(opacity, OpacitySlider.Minimum, OpacitySlider.Maximum);

        Opacity = clamped;
        OpacityValueText.Text = $"{clamped * 100:0}%";

        if (_loading) return;

        _settings.Opacity = clamped;
        _settings.Save();
    }

    // ---- updates ----

    private Available? _update;

    private void WireUpdates()
    {
        CheckUpdateButton.Click += async (_, _) => await CheckForUpdate(announce: true);
        InstallUpdateButton.Click += async (_, _) => await InstallUpdate();

        ViewUpdateButton.Click += (_, _) =>
            Open("https://github.com/JinxyJoshua/JinxyMac-Beta/releases/latest");

        AutoCheckUpdates.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;

            _settings.AutoCheckUpdates = AutoCheckUpdates.IsChecked == true;
            _settings.Save();
        };

        UpdateStatusText.Text = $"Running {Updater.Version}.";
    }

    /// <summary>
    /// Asks whether there is a newer build.
    /// </summary>
    /// <param name="announce">
    /// Whether to say so when there is nothing new. The launch check stays quiet
    /// — an app that reports "you are up to date" every time it opens is noise —
    /// but a button the user pressed has to answer.
    /// </param>
    private async Task CheckForUpdate(bool announce)
    {
        CheckUpdateButton.IsEnabled = false;
        if (announce) UpdateStatusText.Text = "Checking…";

        _update = await Updater.CheckAsync();

        CheckUpdateButton.IsEnabled = true;

        if (_update is not { } update)
        {
            if (announce) UpdateStatusText.Text = $"Running {Updater.Version} — nothing newer.";
            return;
        }

        UpdateStatusText.Text = $"Running {Updater.Version}.";
        UpdateHeadlineText.Text = $"{update.Version} is available  ·  {update.SizeText}";
        UpdateBox.IsVisible = true;
    }

    private async Task InstallUpdate()
    {
        if (_update is not { } update) return;

        InstallUpdateButton.IsEnabled = false;
        CheckUpdateButton.IsEnabled = false;

        UpdateProgress.IsVisible = true;
        UpdateProgress.Value = 0;

        var progress = new Progress<double>(fraction =>
            UpdateProgress.Value = Math.Clamp(fraction, 0, 1));

        UpdateHeadlineText.Text = $"Downloading {update.Version}…";

        string? failure = await Updater.InstallAsync(update, progress);

        if (failure == null)
        {
            // The swap script is waiting for this process to go away before it
            // touches the bundle, so closing is the last step of the install
            // rather than a courtesy.
            UpdateHeadlineText.Text = "Installing. Jinxy will reopen by itself.";
            Close();
            return;
        }

        UpdateProgress.IsVisible = false;
        UpdateHeadlineText.Text = failure;

        InstallUpdateButton.IsEnabled = true;
        CheckUpdateButton.IsEnabled = true;
    }
    // ---- Roblox cache ----

    private void WireCache()
    {
        MeasureCacheButton.Click += async (_, _) => await MeasureCache();
        ClearCacheButton.Click += async (_, _) => await ClearCache();
    }

    /// <summary>
    /// Adds up the cache folders and lists them.
    /// </summary>
    /// <remarks>
    /// On a thread, because a Roblox asset cache is tens of thousands of small
    /// files and walking it takes long enough to freeze the window. The button
    /// says what it is doing rather than going quiet.
    /// </remarks>
    private async Task MeasureCache()
    {
        MeasureCacheButton.IsEnabled = false;
        ClearCacheButton.IsEnabled = false;
        CacheTotalText.Text = "Measuring…";

        IReadOnlyList<CacheFolder> folders = RobloxCache.Folders();

        CacheReport report = await Task.Run(() => RobloxCache.Measure(folders));

        ShowCache(folders);

        CacheTotalText.Text = report.Folders == 0
            ? "Nothing found. Roblox has not run on this machine, or keeps its cache somewhere else."
            : $"{report.SizeText} across {report.Files:N0} files in {report.Folders} folders.";

        MeasureCacheButton.IsEnabled = true;
        ClearCacheButton.IsEnabled = report.Bytes > 0;
    }

    private async Task ClearCache()
    {
        MeasureCacheButton.IsEnabled = false;
        ClearCacheButton.IsEnabled = false;
        CacheTotalText.Text = "Clearing…";

        IReadOnlyList<CacheFolder> folders = RobloxCache.Folders();

        CacheReport report = await Task.Run(() => RobloxCache.Clear(folders));

        ShowCache(folders);

        // Says what was left behind as well as what went. Roblox holds handles
        // on the files it is writing right now, and a total that quietly
        // excluded them would look like the clear had failed.
        CacheTotalText.Text = report.Bytes > 0
            ? $"Freed {report.SizeText} across {report.Files:N0} files. "
              + "Anything Roblox has open right now was left alone."
            : "Nothing to free.";

        if (report.Bytes > 0) Notify.Send("Jinxy AutoClicker", $"Freed {report.SizeText} of Roblox cache.");

        MeasureCacheButton.IsEnabled = true;
        ClearCacheButton.IsEnabled = false;
    }

    /// <summary>Lists the folders and whether each one is actually there.</summary>
    private void ShowCache(IReadOnlyList<CacheFolder> folders)
    {
        CacheList.Items.Clear();

        foreach (CacheFolder folder in folders)
        {
            var label = new TextBlock
            {
                Text = folder.Label,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var state = new TextBlock
            {
                Text = folder.Exists ? folder.Path : "not present",
                FontSize = 10,
                Foreground = this.FindResource("TextMuted") as IBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 460
            };

            var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(state, Dock.Right);
            row.Children.Add(label);
            row.Children.Add(state);

            CacheList.Items.Add(row);
        }
    }

    // ---- menu bar ----

    /// <summary>
    /// A dot in the menu bar that starts and stops the clicker.
    /// </summary>
    /// <remarks>
    /// Avalonia's TrayIcon is an NSStatusItem on macOS and a notification-area
    /// icon on Windows, so this is one implementation for both. It matters more
    /// on the Mac side: the window sits behind a full-screen Roblox, and without
    /// this the only way to reach the clicker is the hotkey.
    ///
    /// The icon is drawn rather than shipped, so the accent colour applies to it
    /// like everything else and there is no asset to keep in step.
    /// </remarks>
    private void WireMenuBar()
    {
        MenuBarEnabled.IsCheckedChanged += (_, _) => RefreshMenuBar();

        MenuBarHint.Text = OperatingSystem.IsMacOS()
            ? "Puts a dot in the menu bar that starts and stops the clicker, so the window does not have to be in front of the game."
            : "Puts an icon in the notification area that starts and stops the clicker. On macOS this is the menu bar.";
    }

    private void RefreshMenuBar()
    {
        bool wanted = MenuBarEnabled.IsChecked == true;

        if (wanted && _tray == null) ShowTray();
        else if (!wanted && _tray != null) HideTray();

        if (_loading) return;

        _settings.MenuBar = wanted;
        _settings.Save();
    }

    private void ShowTray()
    {
        _trayToggle = new NativeMenuItem("Start");
        _trayToggle.Click += (_, _) => Dispatcher.UIThread.Post(Toggle);

        var show = new NativeMenuItem("Show Jinxy");
        show.Click += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Dispatcher.UIThread.Post(Close);

        var accent = this.FindResource("Accent") as ISolidColorBrush;
        _trayLook = (_clicker.IsRunning, accent?.Color ?? Colors.Red);

        _tray = new TrayIcon
        {
            ToolTipText = "Jinxy AutoClicker",
            Icon = TrayImage(_trayLook.Value.Running, _trayLook.Value.Colour),
            IsVisible = true,
            Menu = new NativeMenu { Items = { _trayToggle, new NativeMenuItemSeparator(), show, quit } }
        };

        // Clicking the icon itself opens the menu on macOS, so the click handler
        // is deliberately not a start/stop — a menu bar item that fires an
        // action on a stray click is a menu bar item that starts clicking by
        // accident.
        RefreshTray();
    }

    private void HideTray()
    {
        if (_tray == null) return;

        _tray.IsVisible = false;
        _tray.Dispose();
        _tray = null;
        _trayToggle = null;
        _trayLook = null;
    }

    /// <summary>Keeps the menu entry and the icon in step with the clicker.</summary>
    private void RefreshTray()
    {
        if (_tray == null) return;

        bool running = _clicker.IsRunning;

        if (_trayToggle != null) _trayToggle.Header = running ? "Stop" : "Start";

        _tray.ToolTipText = running
            ? $"Jinxy — running at {CpsSlider.Value:0.0} /s"
            : "Jinxy — idle";

        // Only when it would actually look different. This runs once a second
        // for as long as the app is open, and redrawing meant a bitmap
        // allocation and a PNG encode every tick to produce a picture identical
        // to the one already there.
        var accent = this.FindResource("Accent") as ISolidColorBrush;
        (bool Running, Color Colour) wanted = (running, accent?.Color ?? Colors.Red);

        if (_trayLook == wanted) return;

        _trayLook = wanted;
        _tray.Icon = TrayImage(wanted.Running, wanted.Colour);
    }

    /// <summary>
    /// The menu bar icon: a filled dot in the accent colour, hollow when idle.
    /// </summary>
    /// <remarks>
    /// Drawn at 32px and left for the platform to scale. A shipped PNG would
    /// need a light and a dark variant, a retina variant, and would not follow
    /// the accent — three problems avoided by drawing it.
    /// </remarks>
    private static WindowIcon TrayImage(bool running, Color colour)
    {
        // Disposed: a RenderTargetBitmap holds a GPU surface, and one a second
        // left to the finaliser is a leak with a slow fuse.
        using var bitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            var brush = new SolidColorBrush(colour);
            var pen = new Pen(brush, 4);
            var centre = new Point(16, 16);

            if (running) context.DrawEllipse(brush, null, centre, 11, 11);
            else context.DrawEllipse(null, pen, centre, 9, 9);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;

        return new WindowIcon(stream);
    }

    // ---- settings page ----

    /// <summary>Where clips go: whatever was chosen, or the platform default.</summary>
    private string ClipFolder =>
        string.IsNullOrWhiteSpace(_settings.ClipFolder)
            ? ScreenRecorder.DefaultFolder
            : _settings.ClipFolder;

    private void WireSettings()
    {
        HotkeysEnabled.IsCheckedChanged += (_, _) => ArmHotkeys();

        RecheckPermissionsButton.Click += (_, _) => DescribePermissions();

        OpenAccessPaneButton.Click += (_, _) =>
            Open("x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility");

        OpenScreenPaneButton.Click += (_, _) =>
            Open("x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture");

        AskScreenButton.Click += (_, _) =>
        {
            MacPermissions.RequestScreenRecording();
            DescribePermissions();
        };

        ClipFolderBox.LostFocus += (_, _) => CommitClipFolder();

        ClipFolderBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            CommitClipFolder();
            e.Handled = true;
        };

        BrowseClipFolderButton.Click += async (_, _) => await BrowseClipFolder();

        ResetClipFolderButton.Click += (_, _) =>
        {
            _settings.ClipFolder = "";
            _settings.Save();

            RefreshClipFolder();
        };

        OpenSettingsFolderButton.Click += (_, _) => Reveal(SettingsPath.Folder);

        // Two presses, not a dialog. The second button only appears once the
        // first is pressed, which is enough of a speed bump for something that
        // throws away every setting and the whole click history.
        ResetSettingsButton.Click += (_, _) =>
        {
            bool arming = !ConfirmResetButton.IsVisible;

            ConfirmResetButton.IsVisible = arming;
            ResetHint.IsVisible = arming;
            ResetSettingsButton.Content = arming ? "Cancel" : "Reset settings";
        };

        ConfirmResetButton.Click += (_, _) => ResetEverything();
    }

    private void CommitClipFolder()
    {
        string typed = (ClipFolderBox.Text ?? "").Trim();

        _settings.ClipFolder = typed;
        _settings.Save();

        RefreshClipFolder();
    }

    private async Task BrowseClipFolder()
    {
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Where should clips go?", AllowMultiple = false });

        if (picked.Count == 0) return;

        string? path = picked[0].TryGetLocalPath();

        if (path == null) return;

        _settings.ClipFolder = path;
        _settings.Save();

        RefreshClipFolder();
    }

    /// <summary>
    /// Redraws everything that names the clip folder.
    /// </summary>
    /// <remarks>
    /// Says whether the folder exists yet rather than only where it is. A typo'd
    /// path looks identical to a correct one until the first recording fails,
    /// and by then the clip is gone.
    /// </remarks>
    private void RefreshClipFolder()
    {
        // Saved and restored rather than set to false. This is called from
        // DescribeEngine, which one day will be called from inside a load — and
        // a helper that clears the flag on its way out would let the rest of
        // that load look like the user typing.
        bool was = _loading;
        _loading = true;
        ClipFolderBox.Text = _settings.ClipFolder;
        _loading = was;

        bool custom = !string.IsNullOrWhiteSpace(_settings.ClipFolder);
        bool exists = Directory.Exists(ClipFolder);

        ClipFolderHint.Text =
            "Where recordings and saved replays are written. Left empty it returns to "
            + ScreenRecorder.DefaultFolder + "."
            + (custom && !exists ? "  This folder does not exist yet — it is created on the first recording." : "");

        ClipFolderText.Text = "Clips are saved to " + ClipFolder;
    }

    /// <summary>
    /// Lists every binding in one place, since they are set from three pages.
    /// </summary>
    private void RefreshHotkeySummary()
    {
        string[] bound = Bindings()
            .Where(b => b.Code != 0)
            .Select(b => $"{b.Action}: {b.Name}")
            .ToArray();

        HotkeySummaryText.Text = bound.Length == 0
            ? "Nothing bound yet. The buttons are on the Clicker and Recorder pages."
            : string.Join("      ", bound);
    }

    private void DescribePermissions()
    {
        // Nothing to grant off macOS, and a panel of "not needed" rows is just
        // noise on the platform this is developed on.
        PermissionsCard.IsVisible = OperatingSystem.IsMacOS();

        if (!OperatingSystem.IsMacOS()) return;

        Show(MacPermissions.Accessibility(), AccessibilityState, AccessibilityDetail,
            granted: "Clicks and hotkeys are allowed.",
            denied: "Clicks are being discarded. " + MacPermissions.Where("Accessibility"));

        Permission screen = MacPermissions.ScreenRecording();

        Show(screen, ScreenRecordingState, ScreenRecordingDetail,
            granted: "Recording and instant replay are allowed.",
            denied: "Recordings will be black. " + MacPermissions.Where("Screen Recording"));

        // macOS only shows its own prompt once per install; after that the
        // button is useless and the pane name is the only way through.
        AskScreenButton.IsVisible = screen == Permission.Denied;
        OpenScreenPaneButton.IsVisible = screen == Permission.Denied;
        OpenAccessPaneButton.IsVisible = MacPermissions.Accessibility() == Permission.Denied;

        void Show(Permission state, TextBlock label, TextBlock detail, string granted, string denied)
        {
            bool ok = state == Permission.Granted;

            label.Text = ok ? "GRANTED" : "NOT GRANTED";
            label.Foreground = ok
                ? this.FindResource("Accent") as IBrush
                : this.FindResource("TextMuted") as IBrush;

            detail.Text = ok ? granted : denied;
        }
    }

    private void DescribeEngine()
    {
        string platform = OperatingSystem.IsMacOS() ? "macOS" : "Windows";
        string engine = OperatingSystem.IsMacOS() ? "CGEvent (Quartz)" : "SendInput";

        EngineNote.Text = OperatingSystem.IsMacOS()
            ? "Running on macOS"
            : "Running on Windows — the Mac input path is not exercised here";

        string trouble = _engine.Unavailable is string why ? $"\nNot currently usable: {why}" : "\nReady.";

        EngineDetail.Text = $"Platform: {platform}\nInput: {engine}" + trouble;

        SettingsPathText.Text = SettingsPath.Folder;

        AboutText.Text =
            $"Jinxy AutoClicker — Mac build\n"
            + $"{platform} · .NET {Environment.Version} · Avalonia\n"
            + "Shares the Windows build's timing engine, presets and settings format.";

        RefreshStoredFiles();
        RefreshClipFolder();
        RefreshHotkeySummary();
        DescribePermissions();
    }

    /// <summary>Names the files that exist, so Reset says what it will remove.</summary>
    private void RefreshStoredFiles()
    {
        string[] files = { "settings.json", "click_presets.json", "history.json" };

        string[] present = files
            .Where(f => File.Exists(System.IO.Path.Combine(SettingsPath.Folder, f)))
            .ToArray();

        StoredFilesText.Text = present.Length == 0
            ? "Nothing written yet."
            : "Holding " + string.Join(", ", present) + ".";
    }

    /// <summary>
    /// Deletes every stored file and returns the window to its defaults.
    /// </summary>
    /// <remarks>
    /// Reloads into the live controls rather than asking for a restart. The
    /// settings are all already bound to something on screen, so a restart would
    /// only be a way of avoiding the work.
    /// </remarks>
    private void ResetEverything()
    {
        foreach (string file in new[] { "settings.json", "click_presets.json", "history.json" })
        {
            try { File.Delete(System.IO.Path.Combine(SettingsPath.Folder, file)); }
            catch { /* nothing there, or held open */ }
        }

        var fresh = new AppSettings();

        foreach (System.Reflection.PropertyInfo property in typeof(AppSettings).GetProperties())
        {
            if (property.CanRead && property.CanWrite)
                property.SetValue(_settings, property.GetValue(fresh));
        }

        _settings.Save();

        _presets.Clear();
        _presets.AddRange(PresetStore.Defaults());
        PresetStore.Save(_presets);

        _history.Reset();
        _historyDirty = false;

        ApplySettings();
        BuildPresets();
        RefreshHistory();
        Publish();

        ConfirmResetButton.IsVisible = false;
        ResetHint.IsVisible = false;
        ResetSettingsButton.Content = "Reset settings";

        RefreshStoredFiles();
    }
}
