using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// The macros page: spam one key, or cycle a couple, on a timer.
/// </summary>
/// <remarks>
/// The engine — <see cref="KeyMacro"/>, <see cref="MacroRunner"/>,
/// <see cref="MacroStore"/> — is built and tested elsewhere; this file is the
/// page around it, the same division <c>MainWindow.KitWheel.cs</c> already
/// draws between <see cref="KitWheel"/> and the tiles that show it.
///
/// Split into its own partial file for the same reason every other page is:
/// keeping <c>MainWindow.axaml.cs</c> from growing a few hundred more lines
/// per feature. The three places this file must still touch the main one —
/// <c>Fire</c> needs to dispatch a macro's toggle hotkey, <c>ArmHotkeys</c>
/// needs to watch it, and the constructor needs <c>WireMacros()</c> called —
/// are the smallest edits that could make that true.
/// </remarks>
public partial class MainWindow
{
    private readonly List<KeyMacro> _macroList = MacroStore.Load();

    /// <summary>
    /// The New Macro form's own toggle-hotkey pick, staged until Save bakes it
    /// into a <see cref="KeyMacro"/>.
    /// </summary>
    /// <remarks>
    /// Held here rather than in a field on some not-yet-created macro, for the
    /// same reason the Windows build does it this way: a hotkey can be chosen
    /// before a name has even been typed, and there is nothing yet to attach
    /// it to.
    /// </remarks>
    private HotkeyBinding _pendingNewMacroHotkey = HotkeyBinding.Unbound;

    private bool _macrosBuilt;

    /// <summary>
    /// A can't be bound on this build — see <see cref="HotkeyBinding.Unbound"/>
    /// for why its own code doubles as "no key at all" on macOS. Shared by
    /// both places that can hit it: a typed KEY box (<see cref="SaveMacro"/>)
    /// and a captured toggle hotkey (<see cref="BindMacroHotkey"/>).
    /// </summary>
    private const string UnbindableAMessage =
        "A can't be bound on this build. Its key code doubles as this platform's \"no key\" marker, "
        + "so the app can't tell a bound A from none at all — pick a different letter.";

    private void WireMacros()
    {
        MacrosHotkeyKill.Click += (_, _) =>
            HotkeysEnabled.IsChecked = HotkeysEnabled.IsChecked != true;

        StopAllMacrosButton.Click += (_, _) => StopAllMacros();

        SaveMacroButton.Click += (_, _) => SaveMacro();
        NewMacroHotkeyButton.Click += (_, _) => BindMacroHotkey(NewMacroHotkeyButton, null);

        // Enter in any of the form's own boxes saves it, the same convenience
        // the Kit Wheel's own name boxes already offer.
        foreach (TextBox box in new[] { MacroNameBox, MacroKey1Box, MacroKey2Box, MacroIntervalBox })
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                SaveMacro();
                e.Handled = true;
            };
        }
    }

    /// <summary>
    /// Builds the macro cards the first time the page is actually shown.
    /// </summary>
    /// <remarks>
    /// Matches <c>EnsureKitWheelBuilt</c>: deferred to first arrival rather
    /// than the constructor, even though a handful of macro cards is cheap
    /// enough that it would barely be noticed either way — one convention for
    /// "heavy per-item building" beats deciding fresh on every page.
    /// </remarks>
    private void EnsureMacrosBuilt()
    {
        if (_macrosBuilt) return;
        _macrosBuilt = true;

        RefreshMacroCards();
    }

    // ---- hotkeys ----

    /// <summary>Toggle-hotkey codes for every macro the watcher is allowed to fire.</summary>
    /// <remarks>
    /// A disabled macro's key is left out entirely, not watched-and-ignored —
    /// its key is free to be pressed for anything else, the same rule the
    /// Windows build's poll loop applies.
    /// </remarks>
    private IEnumerable<int> MacroHotkeyCodes() =>
        _macroList.Where(m => m.Enabled && m.Hotkey.IsValid).Select(m => m.Hotkey.Code);

    private KeyMacro? MacroWithHotkey(int code) =>
        _macroList.FirstOrDefault(m => m.Enabled && m.Hotkey.IsValid && m.Hotkey.Code == code);

    /// <summary>
    /// Starts or stops a macro from its toggle hotkey.
    /// </summary>
    /// <remarks>
    /// Looked up again by name rather than trusting the reference <c>Fire</c>
    /// already resolved: a rebind or an interval edit replaces the
    /// <see cref="KeyMacro"/> object (it's immutable), so the copy captured on
    /// the poll thread could be one generation stale by the time this runs.
    ///
    /// Guarded against this window's own text boxes the same way the Windows
    /// build's <c>OnMacroHotkey</c> is: a letter typed into the KEY box while
    /// this window is focused must not also toggle a macro bound to that
    /// letter. It does not — and cannot — guard against the same collision in
    /// some other app, because the watcher polls system-wide independently of
    /// focus; that risk is inherent to a global hotkey and not specific to
    /// macros.
    /// </remarks>
    private void ToggleMacroHotkey(KeyMacro macro)
    {
        if (IsActive && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;

        KeyMacro? current = _macroList.FirstOrDefault(m =>
            string.Equals(m.Name, macro.Name, StringComparison.OrdinalIgnoreCase));

        if (current == null || !current.Enabled) return;

        if (_macros.IsRunning(current.Name)) _macros.Stop(current.Name);
        else _macros.Start(current);

        RefreshMacroCards();
    }

    /// <summary>
    /// Whichever action already holds a key, or null when it is free.
    /// </summary>
    /// <remarks>
    /// Checks the fixed hotkeys first, then every other macro, then — unless
    /// <paramref name="excludingMacro"/> is null, meaning the New Macro form's
    /// own slot is the thing being rebound — the form's own pending pick. That
    /// last exclusion matters: without it, choosing a key for a brand new
    /// macro would immediately collide with itself.
    /// </remarks>
    private string? HotkeyHolder(int code, KeyMacro? excludingMacro)
    {
        string? fixedHolder = Bindings().Where(b => b.Code == code).Select(b => b.Action).FirstOrDefault();
        if (fixedHolder != null) return fixedHolder;

        foreach (KeyMacro m in _macroList)
        {
            if (ReferenceEquals(m, excludingMacro)) continue;
            if (m.Hotkey.IsValid && m.Hotkey.Code == code) return m.Name;
        }

        if (excludingMacro != null && _pendingNewMacroHotkey.IsValid && _pendingNewMacroHotkey.Code == code)
            return "new macro";

        return null;
    }

    /// <summary>
    /// Puts a hotkey button into capture mode and stores whatever is pressed.
    /// </summary>
    /// <param name="macro">
    /// The macro whose card this button belongs to, or null for the New
    /// Macro form's own slot — mirroring how the Windows build tells the two
    /// apart (<c>_rebindingMacro == null</c> there means the same thing).
    /// </param>
    /// <remarks>
    /// Shares <see cref="_rebinding"/> with the fixed hotkeys' own <c>Bind</c>
    /// rather than a second flag — one capture in flight at a time, whichever
    /// button asked for it, is the rule everywhere else in this window and a
    /// macro's toggle hotkey is not a special case.
    /// </remarks>
    private void BindMacroHotkey(Button button, KeyMacro? macro)
    {
        if (_rebinding) return;

        _rebinding = true;

        object? previous = button.Content;
        button.Content = "Press a key…";

        MacroHotkeyNoticeText.IsVisible = false;

        _hotkeys.CaptureNext((code, name) => Dispatcher.UIThread.Post(() =>
        {
            _rebinding = false;

            // Code 0 is both "not set" and, on macOS, the A key's real code
            // (see HotkeyBinding.Unbound) — so a press of A here has to be
            // refused with an explanation, not stored as if nothing had been
            // pressed at all.
            if (code == 0)
            {
                button.Content = previous;
                ShowMacroHotkeyNotice(UnbindableAMessage);
                return;
            }

            string? taken = HotkeyHolder(code, macro);

            if (taken != null)
            {
                button.Content = previous;
                ShowMacroHotkeyNotice($"{name} is already the {taken} key. Pick another.");
                return;
            }

            var binding = new HotkeyBinding(code, name);

            if (macro == null)
            {
                _pendingNewMacroHotkey = binding;
                button.Content = name;
                return;
            }

            int at = _macroList.IndexOf(macro);
            if (at < 0)
            {
                // Deleted out from under the capture. Nothing to attach the
                // key to any more.
                button.Content = previous;
                return;
            }

            // KeyMacro is immutable, so the rebind is a replacement in place —
            // same reason SaveMacro and ToggleMacroEnabled build a new one
            // rather than mutating the old.
            _macroList[at] = new KeyMacro(
                macro.Name, macro.Keys, macro.KeysText, macro.IntervalMs,
                macro.HoldsMs, macro.ClicksWanted, macro.EquipMs,
                binding, macro.Enabled);

            MacroStore.Save(_macroList);

            // Re-arms the watch list with the new binding and rebuilds the
            // cards, in one call — the same one every other macro edit below
            // already ends with.
            ArmHotkeys();
        }));
    }

    private void ShowMacroHotkeyNotice(string message)
    {
        MacroHotkeyNoticeText.Text = message;
        MacroHotkeyNoticeText.IsVisible = true;
    }

    // ---- the list ----

    private void StopAllMacros()
    {
        _macros.StopAll();
        RefreshMacroCards();
    }

    private void DeleteMacro(KeyMacro macro)
    {
        // Stopped before it is forgotten — dropping a running macro from the
        // list without cancelling would leave a thread typing a key with
        // nothing left on screen to switch it off.
        _macros.Stop(macro.Name);

        _macroList.Remove(macro);
        MacroStore.Save(_macroList);

        ArmHotkeys();
    }

    /// <summary>
    /// This macro's own switch, independent of the master hotkeys toggle.
    /// </summary>
    /// <remarks>
    /// Somebody with five macros bound usually wants four of them live and one
    /// out of the way — turning the lot off to silence one key means
    /// remembering which were on before and turning them back on one at a
    /// time.
    /// </remarks>
    private void ToggleMacroEnabled(KeyMacro macro)
    {
        _macros.Stop(macro.Name);

        int at = _macroList.IndexOf(macro);
        if (at < 0) return;

        _macroList[at] = new KeyMacro(
            macro.Name, macro.Keys, macro.KeysText, macro.IntervalMs,
            macro.HoldsMs, macro.ClicksWanted, macro.EquipMs,
            macro.Hotkey, enabled: !macro.Enabled);

        MacroStore.Save(_macroList);

        ArmHotkeys();
    }

    private static bool MentionsUnbindableA(string typed) =>
        typed.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(piece => piece.Trim().Equals("A", StringComparison.OrdinalIgnoreCase));

    private void SaveMacro()
    {
        MacroErrorText.IsVisible = false;

        string name = (MacroNameBox.Text ?? "").Trim();

        if (name.Length == 0)
        {
            ShowMacroError("Give it a name.");
            return;
        }

        // The second box is optional, so it is joined only when it holds
        // something — passing an empty one through would add a trailing
        // separator and fail to parse a perfectly good single key.
        string typed = (MacroKey1Box.Text ?? "").Trim();
        string second = (MacroKey2Box.Text ?? "").Trim();

        if (second.Length > 0) typed += "," + second;

        (int[] Keys, string Text)? keys = MacroStore.ParseKeys(typed);

        if (keys == null)
        {
            ShowMacroError(MentionsUnbindableA(typed)
                ? UnbindableAMessage
                : "Each key box takes one letter or digit — R, or 1, or Q.");
            return;
        }

        int? interval = MacroStore.ParseInterval(MacroIntervalBox.Text);

        if (interval == null)
        {
            ShowMacroError($"Interval must be between {KeyMacro.MinIntervalMs} and {KeyMacro.MaxIntervalMs} ms.");
            return;
        }

        // Replacing a running macro would otherwise leave the old thread going
        // with the old keys, invisibly, its card having been rebuilt.
        _macros.Stop(name);

        MacroStore.Upsert(_macroList, new KeyMacro(
            name, keys.Value.Keys, keys.Value.Text, interval.Value,
            hotkey: _pendingNewMacroHotkey));
        MacroStore.Save(_macroList);

        MacroNameBox.Text = "";
        MacroKey1Box.Text = "";
        MacroKey2Box.Text = "";
        MacroIntervalBox.Text = "";

        // The pending pick has been baked into the macro; the form's slot goes
        // back to Not set so the next macro starts fresh.
        _pendingNewMacroHotkey = HotkeyBinding.Unbound;
        NewMacroHotkeyButton.Content = "Not set";

        ArmHotkeys();
    }

    private void ShowMacroError(string message)
    {
        MacroErrorText.Text = message;
        MacroErrorText.IsVisible = true;
    }

    // ---- painting ----

    private void RefreshMacroRunning()
    {
        int count = _macros.RunningCount;

        MacroRunningText.Text = count switch
        {
            0 => "Nothing running.",
            1 => "1 macro running.",
            _ => $"{count} macros running."
        };
    }

    /// <summary>Repaints the hotkeys card, every macro card, and the running line.</summary>
    private void RefreshMacroCards()
    {
        if (MacroList == null) return;

        bool hotkeysOn = HotkeysEnabled?.IsChecked != false;

        MacrosHotkeyKill.Content = hotkeysOn ? "DISABLE HOTKEYS" : "HOTKEYS ARE OFF";
        MacrosHotkeyKill.Foreground = hotkeysOn
            ? this.FindResource("Text") as IBrush
            : this.FindResource("Accent") as IBrush;

        MacrosHotkeyNote.Text = hotkeysOn
            ? "Every bound key is live. Turn them off while you edit."
            : "All hotkeys are off. Nothing you press will trigger anything.";

        MacroList.Items.Clear();
        foreach (KeyMacro macro in _macroList) MacroList.Items.Add(MacroCard(macro, hotkeysOn));

        RefreshMacroRunning();
    }

    /// <summary>
    /// One macro card: what it sends, how often, its toggle hotkey, and a
    /// switch.
    /// </summary>
    /// <remarks>
    /// Built in code rather than as a DataTemplate, the same call the Windows
    /// build makes and for the same reason: the running switch reflects the
    /// engine, and whether a macro is running is a fact about the process
    /// rather than a property of the macro sitting in <see cref="_macroList"/>.
    /// </remarks>
    private Border MacroCard(KeyMacro macro, bool hotkeysOn)
    {
        // Two ways to be off, shown the same way — which one it is changes the
        // wording, not whether the macro can run.
        bool live = hotkeysOn && macro.Enabled;

        var name = new TextBlock
        {
            Text = macro.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = this.FindResource("TextBright") as IBrush
        };

        var summary = new TextBlock
        {
            Text = macro.SummaryText,
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = this.FindResource("Accent") as IBrush
        };

        var toggle = new CheckBox
        {
            IsChecked = _macros.IsRunning(macro.Name),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0),
            // Nothing starts while hotkeys are off, or while this macro's own
            // switch is off — the toggle is the last way in once either kill
            // is thrown, so leaving it live would make DISABLED a claim the
            // card does not keep.
            IsEnabled = live,
            [ToolTip.TipProperty] = live
                ? null
                : (object)(macro.Enabled ? "Hotkeys are switched off" : "This macro is disabled")
        };

        toggle.IsCheckedChanged += (_, _) =>
        {
            if (toggle.IsChecked == true) _macros.Start(macro);
            else _macros.Stop(macro.Name);

            RefreshMacroRunning();
        };

        var hotkeyLabel = new TextBlock
        {
            Text = live ? "TOGGLE HOTKEY" : "TOGGLE HOTKEY — OFF",
            FontSize = 9,
            FontWeight = FontWeight.Bold,
            Foreground = this.FindResource(live ? "TextMuted" : "Accent") as IBrush,
            Margin = new Thickness(0, 10, 0, 4)
        };

        var hotkeyButton = new Button
        {
            Content = macro.Hotkey.Name,
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            // Dimmed rather than disabled: rebinding while hotkeys are off is
            // exactly when someone would want to, since nothing can fire
            // mid-edit.
            Opacity = live ? 1.0 : 0.45,
            [ToolTip.TipProperty] = live
                ? "Click, then press a key or mouse side button"
                : "This macro will not fire"
        };

        hotkeyButton.Click += (_, _) => BindMacroHotkey(hotkeyButton, macro);

        var enable = new Button
        {
            Content = macro.Enabled ? "Disable" : "Enable",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11,
            Margin = new Thickness(0, 10, 8, 0)
        };
        enable.Click += (_, _) => ToggleMacroEnabled(macro);

        var remove = new Button
        {
            Content = "Delete",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 0)
        };
        remove.Click += (_, _) => DeleteMacro(macro);

        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toggle, Dock.Right);
        header.Children.Add(toggle);
        header.Children.Add(name);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(enable);
        buttons.Children.Add(remove);

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(summary);
        body.Children.Add(hotkeyLabel);
        body.Children.Add(hotkeyButton);
        body.Children.Add(buttons);

        // With hotkeys off — or this one macro disabled — the card is stamped
        // across rather than only saying so in small type. A card that still
        // lists a key and a running switch looks armed, and "why did my key
        // stop working" is the question this exists to answer before it gets
        // asked.
        var stamp = new Grid();
        stamp.Children.Add(body);

        if (!live)
        {
            body.Opacity = 0.45;

            stamp.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0xE0, 0x2B, 0x2B)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x2B, 0x2B)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0, 6, 0, 6),
                // Never takes a click. The delete button and the running
                // switch stay reachable with hotkeys off — turning them off is
                // exactly when someone is editing.
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = macro.Enabled ? "HOTKEYS OFF" : "DISABLED",
                    FontSize = 20,
                    FontWeight = FontWeight.Black,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            });
        }

        return new Border
        {
            Background = this.FindResource("Control") as IBrush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 10, 10),
            Width = 220,
            Child = stamp
        };
    }
}
