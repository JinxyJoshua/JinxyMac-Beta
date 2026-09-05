using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// The kit randomizer page: tick a set of kits, then roll through it one at a time.
/// </summary>
/// <remarks>
/// Two lists, not one, exactly as <see cref="KitWheel"/> models it. The roster
/// is every kit someone might play — the game has well over a hundred — and
/// the wheel is the handful ticked for this run. Rolls without replacement:
/// the run is getting through the ticked set once, so a rolled kit leaves the
/// pool and the remaining count actually counts down.
///
/// Split into its own partial file, the way Presets and the other pages
/// already keep their wiring out of <c>MainWindow.axaml.cs</c>. The choosing
/// and counting live in <see cref="KitWheel"/> and are tested there; this file
/// is the page around it.
///
/// The WPF original animated the reel with a <c>DispatcherTimer</c> driving
/// manual <c>DoubleAnimation</c>s. Avalonia's equivalent is a
/// <c>DispatcherTimer</c> (used the same way elsewhere in this app, for the
/// stats tick) driving <see cref="Transitions"/> on each layer's
/// <see cref="Visual.OpacityProperty"/> — a real crossfade, not a name that
/// jumps, and it still slows into its landing the way the source did. What
/// did not carry over is the small overshoot "pop" the WPF version played on
/// the result once it settled; the crossfade landing on the winner already
/// reads as an arrival, and chasing a scale-transform bounce through
/// Avalonia's transform/animation plumbing was not worth the fragility for a
/// finishing touch. See the task-3 report for the reasoning.
/// </remarks>
public partial class MainWindow
{
    private readonly KitRoster _kitRoster = KitWheelStore.Load();
    private readonly List<string> _kitRolled = new();
    private bool _kitRolling;

    /// <summary>The single random source every roll and every reel uses.</summary>
    /// <remarks>
    /// <see cref="KitWheel"/> deliberately owns no <see cref="Random"/> of its
    /// own — it takes a picker instead, so a test can pin the outcome. This is
    /// the one instance the page supplies, shared across every roll and every
    /// scenery pick in the reel, rather than a fresh one each time.
    /// </remarks>
    private readonly Random _kitRandom = new();

    /// <summary>
    /// Decoded pictures, kept for the life of the window.
    /// </summary>
    /// <remarks>
    /// The roster repaints on every tick, roll and removal, and decoding every
    /// tile's picture each time would make ticking a checkbox visibly slow.
    /// Cleared for one kit when its picture changes rather than wholesale.
    /// </remarks>
    private readonly Dictionary<string, Bitmap?> _kitArtCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource _kitArtCts = new();
    private bool _kitArtStarted;
    private bool _kitListAutoOpened;
    private bool _kitWheelBuilt;
    private string _kitSearch = "";

    /// <summary>The reel's own timer, while a roll is in flight.</summary>
    /// <remarks>
    /// Kept so <c>Closed</c> can stop it if the window shuts mid-roll — the
    /// same reasoning <see cref="_kitArtCts"/> already gets for the network
    /// fetch, just missing here until now. Cleared the moment the reel stops,
    /// whether that is the tick handler finishing normally or the window
    /// closing early, so it is never stopped twice and never outlives the
    /// roll it belongs to.
    /// </remarks>
    private DispatcherTimer? _kitRollTicker;

    /// <summary>Which of the two reel layers is currently in front.</summary>
    private bool _kitReelOnA = true;

    /// <summary>
    /// The kits that came with the app, which cannot be deleted.
    /// </summary>
    /// <remarks>
    /// Only what somebody typed in themselves can be removed. Deleting a real
    /// kit off the roster is almost always a misclick — it is one of a hundred
    /// small tiles — and unticking already does the thing people actually
    /// want, which is keeping it off the wheel.
    /// </remarks>
    private static readonly HashSet<string> ShippedKits =
        new(KitWheel.StarterRoster(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The headline's usual size, and the smaller one a whole sentence needs.</summary>
    private const double HeadlineSize = 34;
    private const double FirstRunHeadlineSize = 20;

    /// <summary>How many kits go past before the reel settles.</summary>
    private const int ReelLength = 14;

    /// <summary>Gap between kits at the start of the roll, and at the end.</summary>
    /// <remarks>
    /// The gap grows across the reel rather than staying put. A fixed interval
    /// reads as a strobe — every frame the same, then an abrupt stop — where a
    /// reel that slows into its result reads as one motion coming to rest.
    /// </remarks>
    private const double ReelFastMs = 30;
    private const double ReelSlowMs = 130;

    private void WireKitWheel()
    {
        RollButton.Click += (_, _) => RollKit();
        ResetRunButton.Click += (_, _) => ResetKitRun();

        SaveWheelButton.Click += (_, _) => SaveCurrentWheel();
        WheelNameBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            SaveCurrentWheel();
            e.Handled = true;
        };

        KitListToggle.Click += (_, _) => SetKitListOpen(!KitListBody.IsVisible);

        KitSearchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            _kitSearch = KitSearchBox.Text ?? "";
            RefreshKitWheel();
        };

        SelectAllKitsButton.Click += (_, _) => SelectAllKits();
        ClearKitsButton.Click += (_, _) => ClearKits();

        AddKitButton.Click += (_, _) => AddTypedKit();
        KitNameBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            AddTypedKit();
            e.Handled = true;
        };
    }

    /// <summary>
    /// Builds the roster tiles the first time the page is actually shown.
    /// </summary>
    /// <remarks>
    /// A tile per kit means decoding and compositing a picture for the whole
    /// roster — real work, on the UI thread. Run from the constructor that
    /// would mean every launch pays it before the window is even visible, for
    /// a page most sessions never open. Deferred to first arrival at the
    /// page instead, the same way <see cref="FetchMissingKitArtAsync"/> and
    /// <see cref="OpenKitListIfNothingPicked"/> already are — both gated on
    /// arriving at <c>PageKitWheel</c> inside <c>Show</c>, not on launch.
    /// </remarks>
    private void EnsureKitWheelBuilt()
    {
        if (_kitWheelBuilt) return;
        _kitWheelBuilt = true;

        RefreshKitWheel();
    }

    // ---- fetching missing pictures ----

    /// <summary>
    /// Pulls down any kit pictures this copy of the app has not got.
    /// </summary>
    /// <remarks>
    /// The install ships a picture for every kit on the starter roster, so the
    /// usual outcome is that this finds nothing missing and never touches the
    /// network. It covers what the installer cannot: a kit added by hand, or
    /// one whose picture failed to install.
    /// </remarks>
    private async Task FetchMissingKitArtAsync()
    {
        if (_kitArtStarted) return;

        // Switchable from the published config, for the case where the wiki
        // changes shape and every fetch starts saving rubbish. Turning it off
        // beats waiting for everyone to install a fix. Left unset rather than
        // latched, so a config published later in the same session is picked
        // up on the next visit to the page instead of needing a restart.
        if (!RemoteConfig.Current.KitArtFetchEnabled) return;

        _kitArtStarted = true;

        await KitArtFetch.RunAsync(
            _kitRoster.Kits,
            batchDone: () =>
            {
                // Cached nulls from before the download have to go, or the
                // tiles keep showing placeholders for pictures now on disk.
                foreach (Bitmap? cached in _kitArtCache.Values) DisposeKitArt(cached);
                _kitArtCache.Clear();
                RefreshKitWheel();

                return Task.CompletedTask;
            },
            _kitArtCts.Token).ConfigureAwait(true);
    }

    // ---- the roster ----

    private void AddTypedKit()
    {
        string? name = KitWheel.CleanName(KitNameBox.Text);

        if (name != null && KitWheel.Add(_kitRoster.Kits, name))
        {
            // Ticked on the way in. Somebody who just typed a kit's name
            // wants it in this run; making them tick it as a second step is a
            // step that would never be skipped.
            _kitRoster.Selected.Add(name);

            KitNameBox.Text = "";
            KitAddHintText.IsVisible = false;
            SaveKitRoster();
            RefreshKitWheel();
            return;
        }

        KitAddHintText.Text = name == null ? "Type a kit name" : "Already on the roster";
        KitAddHintText.IsVisible = true;
    }

    private void RemoveKit(string kit)
    {
        if (_kitRolling || ShippedKits.Contains(kit)) return;

        _kitRoster.Kits.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));
        _kitRoster.Selected.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));
        _kitRolled.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));

        SaveKitRoster();
        RefreshKitWheel();
    }

    /// <summary>Clicking a tile puts the kit on the wheel, or takes it off.</summary>
    private void KitTileClick(string kit)
    {
        if (_kitRolling) return;

        bool picked = _kitRoster.Selected.Any(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));

        _kitRoster.Selected.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));

        if (picked)
        {
            // Taking a kit off the wheel takes it out of the run as well, or
            // the progress count would keep counting something no longer on it.
            _kitRolled.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _kitRoster.Selected.Add(kit);
        }

        SaveKitRoster();
        RefreshKitWheel();
    }

    private void SaveKitRoster() => KitWheelStore.Save(_kitRoster);

    // ---- searching the roster ----

    private List<string> ShownKits() => KitWheel.Matching(_kitRoster.Kits, _kitSearch);

    private bool Searching => _kitSearch.Trim().Length > 0;

    /// <summary>
    /// Ticks everything the list is showing.
    /// </summary>
    /// <remarks>
    /// Scoped to the search results when there is a search, which is the
    /// point of having one. Adds to the selection rather than replacing it,
    /// so selecting the matches for one search and then another accumulates
    /// instead of the second wiping the first.
    /// </remarks>
    private void SelectAllKits()
    {
        if (_kitRolling) return;

        if (!Searching)
        {
            _kitRoster.Selected = new List<string>(_kitRoster.Kits);
        }
        else
        {
            foreach (string kit in ShownKits())
            {
                if (!_kitRoster.Selected.Any(s => s.Equals(kit, StringComparison.OrdinalIgnoreCase)))
                    _kitRoster.Selected.Add(kit);
            }
        }

        SaveKitRoster();
        RefreshKitWheel();
    }

    /// <summary>
    /// Unticks everything the list is showing.
    /// </summary>
    /// <remarks>
    /// Only the matches while searching, and the rolled list is only emptied
    /// on a full clear — dropping a few kits mid-run should not restart it.
    /// </remarks>
    private void ClearKits()
    {
        if (_kitRolling) return;

        if (!Searching)
        {
            _kitRoster.Selected.Clear();
            _kitRolled.Clear();
        }
        else
        {
            foreach (string kit in ShownKits())
            {
                _kitRoster.Selected.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));
                _kitRolled.RemoveAll(k => k.Equals(kit, StringComparison.OrdinalIgnoreCase));
            }
        }

        SaveKitRoster();
        RefreshKitWheel();
    }

    // ---- saved wheels ----

    private void SaveCurrentWheel()
    {
        if (_kitRolling) return;

        if (KitWheel.SavePreset(_kitRoster.Presets, WheelNameBox.Text, _kitRoster.Selected))
        {
            WheelNameBox.Text = "";
            WheelHintText.IsVisible = false;
            SaveKitRoster();
            RefreshKitWheel();
            return;
        }

        // Said on the hint line rather than a dialog: every way this fails is
        // already visible on the page.
        WheelHintText.Text =
            _kitRoster.Selected.Count == 0 ? "Pick some kits first"
            : KitWheel.CleanName(WheelNameBox.Text) == null ? "Give it a name"
            : $"{KitWheel.MaxPresets} saved wheels is the limit";
        WheelHintText.IsVisible = true;
    }

    private void LoadWheel(string name)
    {
        if (_kitRolling) return;

        KitPreset? preset = _kitRoster.Presets
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (preset == null) return;

        _kitRoster.Selected = KitWheel.ApplyPreset(_kitRoster.Kits, preset.Kits);

        // Loading a wheel starts that wheel. Keeping the rolled list would
        // leave a run half finished against a set it was never run against.
        _kitRolled.Clear();

        RollBadgeText.Text = "KIT ROLL";
        RollHeadlineText.Text = "READY?";
        RollHeadlineText.FontSize = HeadlineSize;
        RollSubText.Text = $"Loaded {preset.Name}";
        ReelStack.IsVisible = false;
        RollHeadlineText.IsVisible = true;
        ReelBar.Value = 0;
        ReelBar.IsVisible = false;

        SaveKitRoster();
        RefreshKitWheel();
    }

    private void DeleteWheel(string name)
    {
        if (_kitRolling) return;

        _kitRoster.Presets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        SaveKitRoster();
        RefreshKitWheel();
    }

    // ---- kit pictures ----

    /// <summary>Every extension <see cref="KitImages"/> will read back.</summary>
    /// <remarks>
    /// <c>KitImages.FileFilter</c> is a pipe-delimited string built for a
    /// Windows file-picker filter, not the shape Avalonia's
    /// <see cref="FilePickerFileType"/> takes — kept here as its own small
    /// list rather than parsed out of that string.
    /// </remarks>
    private static readonly string[] KitImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

    private static FilePickerFileType KitImageFileType => new("Images")
    {
        Patterns = KitImageExtensions.Select(e => "*" + e).ToArray()
    };

    private async Task SetKitImageAsync(string kit)
    {
        try
        {
            IReadOnlyList<IStorageFile> picked = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = $"Picture for {kit}",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { KitImageFileType }
                });

            string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
            if (path == null) return;

            if (KitImages.Set(kit, path) == null)
            {
                KitPictureStatusText.Text =
                    "That picture could not be used. It may be open elsewhere, or in a format this app cannot read.";
                KitPictureStatusText.IsVisible = true;
                return;
            }

            // Only this kit's cached picture is stale.
            if (_kitArtCache.Remove(kit, out Bitmap? stale)) DisposeKitArt(stale);
            KitPictureStatusText.IsVisible = false;

            RefreshKitWheel();
        }
        catch
        {
            // A cancelled or failed pick leaves the previous picture alone.
        }
    }

    /// <summary>Reads a kit's picture and puts it on the backdrop, ready to show.</summary>
    private Bitmap? LoadKitImage(string kit)
    {
        if (_kitArtCache.TryGetValue(kit, out Bitmap? cached)) return cached;

        string? path = KitImages.Find(kit);
        Bitmap? decoded = path == null ? null : KitArtImage.Decode(path);
        Bitmap? art = decoded == null ? null : KitArtImage.OnBackdrop(decoded);

        _kitArtCache[kit] = art;

        return art;
    }

    /// <summary>Disposes a cached kit picture, unless a reel layer is still showing it.</summary>
    /// <remarks>
    /// Dropping a <see cref="Bitmap"/> from <see cref="_kitArtCache"/> without
    /// disposing it just leaves the decode for the finalizer, so this is
    /// called on both places the cache drops one — <see cref="SetKitImageAsync"/>
    /// and the missing-art fetch's cache clear.
    ///
    /// The tiles that <see cref="RefreshKitWheel"/> builds are rebuilt in the
    /// same call that clears the cache, so nothing there is left pointing at
    /// a disposed picture for longer than the rest of that one method. The
    /// two reel layers are different: <see cref="ReelImageA"/> and
    /// <see cref="ReelImageB"/> are named elements that keep whatever picture
    /// they last received — including after a roll settles and the reel
    /// itself goes invisible — completely independently of the roster
    /// rebuild. Disposing a bitmap still assigned to one of them would pull
    /// the settled result's picture out from under it, so anything referenced
    /// there is left alone and still finalizes normally.
    /// </remarks>
    private void DisposeKitArt(Bitmap? bitmap)
    {
        if (bitmap == null) return;
        if (ReferenceEquals(bitmap, ReelImageA.Source)) return;
        if (ReferenceEquals(bitmap, ReelImageB.Source)) return;

        bitmap.Dispose();
    }

    // ---- opening and closing the roster ----

    private void SetKitListOpen(bool open)
    {
        KitListBody.IsVisible = open;
        KitListChevron.Text = open ? "▾" : "▸";

        if (!open) return;

        // Fades in rather than appearing, so a list this tall does not read
        // as the page having jumped.
        KitListBody.Opacity = 0;
        KitListBody.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(140) }
        };
        KitListBody.Opacity = 1;
    }

    /// <summary>
    /// Opens the roster by itself when there is nothing on the wheel yet.
    /// </summary>
    /// <remarks>
    /// Runs at most once per launch, so someone who deliberately closes it is
    /// not overruled every time they come back to the page, and only fires
    /// with nothing picked, so it stops happening the moment the wheel has
    /// kits on it.
    /// </remarks>
    private void OpenKitListIfNothingPicked()
    {
        if (_kitListAutoOpened) return;

        _kitListAutoOpened = true;

        if (_kitRoster.Selected.Count > 0) return;

        SetKitListOpen(true);
    }

    /// <summary>
    /// The picked kits, as one line for the closed header.
    /// </summary>
    /// <remarks>
    /// Names rather than a count, because the count is already on the right
    /// and "9 selected" does not answer the question anyone actually has when
    /// the list is shut, which is <em>which</em> nine.
    /// </remarks>
    private static string SelectionSummary(IReadOnlyList<string> chosen, int maxChars = 70)
    {
        if (chosen.Count == 0) return "Nothing picked yet";

        string joined = string.Join(", ", chosen);

        if (joined.Length <= maxChars) return joined;

        // Trimmed on a name boundary; a summary cut mid-word reads as a bug.
        var kept = new List<string>();
        int used = 0;

        foreach (string kit in chosen)
        {
            if (used + kit.Length + 2 > maxChars) break;

            kept.Add(kit);
            used += kit.Length + 2;
        }

        int hidden = chosen.Count - kept.Count;

        return kept.Count == 0
            ? $"{chosen.Count} kits picked"
            : string.Join(", ", kept) + $" +{hidden} more";
    }

    // ---- building the tiles ----

    /// <summary>
    /// One kit tile: its picture (or a placeholder), its name, and — for a
    /// kit somebody typed in themselves — a way to delete it.
    /// </summary>
    /// <remarks>
    /// The whole tile is the switch. Modelled on <c>PresetCard</c>: an outer
    /// <see cref="Border"/> that applies on <see cref="InputElement.PointerPressed"/>,
    /// with an inner delete button that marks its own click handled so the
    /// press does not also toggle the tile underneath it on the way past.
    /// </remarks>
    private Control KitTile(string kit)
    {
        bool picked = _kitRoster.Selected.Any(s => s.Equals(kit, StringComparison.OrdinalIgnoreCase));
        bool rolled = _kitRolled.Any(r => r.Equals(kit, StringComparison.OrdinalIgnoreCase));
        bool shipped = ShippedKits.Contains(kit);

        Bitmap? art = LoadKitImage(kit);

        Control face = art != null
            ? new Image { Source = art, Stretch = Stretch.UniformToFill }
            : new TextBlock
            {
                Text = kit.Length > 0 ? kit[..1].ToUpperInvariant() : "?",
                FontSize = 22,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = this.FindResource("TextMuted") as IBrush
            };

        var picture = new Border
        {
            Width = 76,
            Height = 76,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = this.FindResource("Panel2") as IBrush,
            Child = face
        };

        var name = new TextBlock
        {
            Text = kit,
            FontSize = 11,
            Margin = new Thickness(4, 7, 4, 0),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = this.FindResource(picked ? "TextBright" : "TextDim") as IBrush
        };

        var body = new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 0),
            Opacity = rolled ? 0.4 : 1.0,
            Children = { picture, name }
        };

        var overlay = new Grid { Children = { body } };

        // Only on kits somebody added. The shipped roster has no delete.
        if (!shipped)
        {
            Button remove = SmallButton("✕", "Delete this kit from the roster");
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            remove.VerticalAlignment = VerticalAlignment.Top;

            remove.Click += (_, e) =>
            {
                e.Handled = true;
                RemoveKit(kit);
            };

            overlay.Children.Add(remove);
        }

        var tip = rolled
            ? $"{kit} — already rolled this run"
            : picked
                ? $"{kit} — on the wheel. Click to remove, right-click for a picture."
                : $"{kit} — click to add. Right-click for a picture.";

        var card = new Border
        {
            Width = 100,
            Height = 122,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 8, 8),
            BorderThickness = new Thickness(picked ? 2 : 1),
            BorderBrush = this.FindResource(picked ? "Accent" : "Hairline") as IBrush,
            Background = this.FindResource(picked ? "Panel" : "Sunken") as IBrush,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = overlay,
            [ToolTip.TipProperty] = tip
        };

        card.PointerPressed += (_, e) =>
        {
            if (e.Handled) return;
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;

            KitTileClick(kit);
        };

        var setPicture = new MenuItem { Header = "Set picture…" };
        setPicture.Click += (_, _) => _ = SetKitImageAsync(kit);

        card.ContextMenu = new ContextMenu { Items = { setPicture } };

        return card;
    }

    /// <summary>One saved wheel: its name and kit count, loaded on click, deletable beside it.</summary>
    private Control WheelChip(KitPreset preset)
    {
        var name = new TextBlock
        {
            Text = preset.Name,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = this.FindResource("TextBright") as IBrush
        };

        var count = new TextBlock
        {
            Text = preset.Kits.Count == 1 ? "1 kit" : $"{preset.Kits.Count} kits",
            FontSize = 11,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = this.FindResource("Accent") as IBrush
        };

        var tip = $"Load {preset.Name} — {string.Join(", ", preset.Kits.Take(8))}"
            + (preset.Kits.Count > 8 ? $" +{preset.Kits.Count - 8} more" : "");

        var load = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 7),
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, count } },
            [ToolTip.TipProperty] = tip
        };
        load.Click += (_, _) => LoadWheel(preset.Name);

        Button remove = SmallButton("✕", "Delete this saved wheel");
        remove.Click += (_, e) =>
        {
            e.Handled = true;
            DeleteWheel(preset.Name);
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { load, remove } };

        return new Border
        {
            Background = this.FindResource("Sunken") as IBrush,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = this.FindResource("Hairline") as IBrush,
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 8, 8),
            Child = row
        };
    }

    // ---- repainting the page ----

    /// <summary>Repaints the roster, the saved wheels, the counters and the progress bar.</summary>
    private void RefreshKitWheel()
    {
        if (KitList == null) return;

        List<string> chosen = _kitRoster.Selected;

        // Only the matches are shown, but every count below still speaks for
        // the whole roster — a search narrows the list, not the wheel.
        List<string> shown = ShownKits();

        KitList.Items.Clear();
        foreach (string kit in shown) KitList.Items.Add(KitTile(kit));

        WheelList.Items.Clear();
        foreach (KitPreset preset in _kitRoster.Presets) WheelList.Items.Add(WheelChip(preset));

        WheelEmptyText.IsVisible = _kitRoster.Presets.Count == 0;

        int left = KitWheel.Remaining(chosen, _kitRolled).Count;

        KitCountText.Text = $"{chosen.Count} selected";
        KitSelectionText.Text = SelectionSummary(chosen);
        KitsRemainingText.Text = $"{left} KIT{(left == 1 ? "" : "S")} REMAINING";

        // An empty roster and a search that found nothing look identical on
        // screen but mean opposite things, so they do not share a message.
        KitEmptyText.Text = _kitRoster.Kits.Count == 0
            ? "No kits on the roster. Type one above and press Add kit."
            : $"No kit matches “{_kitSearch.Trim()}”.";
        KitEmptyText.IsVisible = shown.Count == 0;

        // The buttons say which they will act on, because either meaning is a
        // reasonable guess and guessing wrong on Clear loses a selection.
        SelectAllKitsButton.Content = Searching ? "Select matches" : "Select all";
        ClearKitsButton.Content = Searching ? "Clear matches" : "Clear";

        ChallengeProgressText.Text = $"{_kitRolled.Count} / {chosen.Count}";
        ChallengeProgressBar.Value = KitWheel.Progress(chosen.Count, _kitRolled.Count);

        bool finished = KitWheel.IsComplete(chosen, _kitRolled);

        RollButton.IsEnabled = KitWheel.CanSpin(chosen) && !finished && !_kitRolling;
        ResetRunButton.IsEnabled = _kitRolled.Count > 0 && !_kitRolling;

        if (_kitRolling) return;

        if (finished)
        {
            RollBadgeText.Text = "RUN COMPLETE";
            RollHeadlineText.Text = "ALL DONE";
            RollHeadlineText.FontSize = HeadlineSize;
            RollSubText.Text = $"All {chosen.Count} of your picked kits have been played";
        }
        else if (chosen.Count == 0)
        {
            // What a fresh install opens on. "READY?" is a lie when nothing
            // is picked, so the panel asks for the one thing it needs instead.
            RollBadgeText.Text = "GET STARTED";
            RollHeadlineText.Text = "Choose your kits for the wheel";
            RollHeadlineText.FontSize = FirstRunHeadlineSize;
            RollSubText.Text = "Open the list below and pick the ones you want";
        }
        else if (!KitWheel.CanSpin(chosen))
        {
            RollBadgeText.Text = "KIT ROLL";
            RollHeadlineText.Text = "READY?";
            RollHeadlineText.FontSize = HeadlineSize;
            RollSubText.Text = $"One more — a wheel needs at least {KitWheel.MinKits}";
        }
        else
        {
            // Leaves the headline text as it is — after a roll that is the
            // winner's name, and repainting it back to "READY?" on every tick
            // or search keystroke would erase the result the roll just gave.
            RollHeadlineText.FontSize = HeadlineSize;
        }
    }

    // ---- the run ----

    private void ResetKitRun()
    {
        if (_kitRolling) return;

        _kitRolled.Clear();

        RollBadgeText.Text = "KIT ROLL";
        RollHeadlineText.Text = "READY?";
        RollHeadlineText.FontSize = HeadlineSize;
        RollSubText.Text = "Press roll to begin";
        ReelStack.IsVisible = false;
        RollHeadlineText.IsVisible = true;
        ReelBar.Value = 0;
        ReelBar.IsVisible = false;

        RefreshKitWheel();
    }

    private void RollKit()
    {
        if (_kitRolling) return;

        string? winner = KitWheel.Roll(_kitRoster.Selected, _kitRolled, _kitRandom.Next);
        if (winner == null) return;

        // Decided before the flicker starts, so the names going past are
        // decoration and cannot change the outcome.
        List<string> pool = KitWheel.Remaining(_kitRoster.Selected, _kitRolled);

        _kitRolling = true;
        RollButton.IsEnabled = false;
        ResetRunButton.IsEnabled = false;

        RollBadgeText.Text = "ROLLING";
        RollSubText.Text = "";

        // Built up front and ending on the winner, so the last thing shown is
        // the result rather than a coincidence of where the timer stopped.
        var reel = new List<string>();
        for (int i = 0; i < ReelLength; i++) reel.Add(pool[_kitRandom.Next(pool.Count)]);
        reel.Add(winner);

        ReelBar.Value = 0;
        ReelBar.IsVisible = true;
        ReelStack.IsVisible = true;
        RollHeadlineText.IsVisible = false;

        int at = 0;
        var ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ReelFastMs) };
        _kitRollTicker = ticker;

        ticker.Tick += (_, _) =>
        {
            if (at >= reel.Count)
            {
                ticker.Stop();

                // Cleared on the same path that stopped it, so a window
                // close afterward finds nothing left to stop and a roll that
                // finishes normally never leaks its timer.
                _kitRollTicker = null;

                Settle(winner);
                return;
            }

            ShowPassingKit(reel[at], ticker.Interval.TotalMilliseconds);

            // Cubed, so it holds its speed for most of the reel and then
            // loses it quickly at the end — a linear slowdown feels like
            // dragging rather than arriving.
            double t = (at + 1) / (double)reel.Count;
            double eased = 1 - Math.Pow(1 - t, 3);

            // Filled by how far along the reel is, not by time. The reel
            // slows down, so a time-based bar would race ahead of it.
            ReelBar.Value = t;

            ticker.Interval = TimeSpan.FromMilliseconds(
                ReelFastMs + (ReelSlowMs - ReelFastMs) * eased);

            at++;
        };

        ticker.Start();
    }

    /// <summary>
    /// Dissolves one kit into the next.
    /// </summary>
    /// <param name="gapMs">
    /// How long this kit is on screen. The crossfade is scaled to it rather
    /// than fixed: a set duration is longer than the gap at the start of the
    /// reel, so the fades would stack and nothing is ever fully drawn.
    /// </param>
    private void ShowPassingKit(string kit, double gapMs)
    {
        // The layer that is currently invisible takes the new kit, then the
        // two trade places. Nothing is ever blank: the outgoing kit is still
        // there at full strength as the incoming one arrives over it.
        StackPanel incoming = _kitReelOnA ? ReelLayerB : ReelLayerA;
        StackPanel outgoing = _kitReelOnA ? ReelLayerA : ReelLayerB;
        Image target = _kitReelOnA ? ReelImageB : ReelImageA;
        TextBlock label = _kitReelOnA ? ReelNameB : ReelNameA;

        label.Text = kit;
        target.Source = LoadKitImage(kit);

        var duration = TimeSpan.FromMilliseconds(Math.Clamp(gapMs * 0.85, 30.0, 240.0));
        var easing = new CubicEaseInOut();

        incoming.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = easing }
        };
        outgoing.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = easing }
        };

        incoming.Opacity = 1.0;
        outgoing.Opacity = 0.0;

        _kitReelOnA = !_kitReelOnA;
    }

    /// <summary>Puts a kit on the front layer with no transition.</summary>
    private void SetReelKit(string kit)
    {
        StackPanel front = _kitReelOnA ? ReelLayerA : ReelLayerB;
        StackPanel back = _kitReelOnA ? ReelLayerB : ReelLayerA;

        (_kitReelOnA ? ReelNameA : ReelNameB).Text = kit;
        (_kitReelOnA ? ReelImageA : ReelImageB).Source = LoadKitImage(kit);

        front.Transitions = null;
        back.Transitions = null;
        front.Opacity = 1;
        back.Opacity = 0;
    }

    /// <summary>Lands on the rolled kit and marks it played.</summary>
    private void Settle(string winner)
    {
        _kitRolled.Add(winner);
        _kitRolling = false;

        // Refreshed first, then the result written over it: the refresh
        // paints from the roster's state, which has no idea a roll just
        // happened.
        RefreshKitWheel();

        SetReelKit(winner);

        if (!KitWheel.IsComplete(_kitRoster.Selected, _kitRolled))
        {
            RollBadgeText.Text = "KIT LOCKED IN";
            RollHeadlineText.Text = winner;
            RollSubText.Text = "Play this one, then roll again";
        }
        else
        {
            RollBadgeText.Text = "RUN COMPLETE";
            RollHeadlineText.Text = winner;
            RollSubText.Text = "That was the last one — run complete";
        }
    }
}
