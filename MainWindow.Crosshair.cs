using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// The Crosshair page: a gallery of ready-made crosshairs, one you can build or
/// import, and the two buttons that write the chosen one into Roblox's cursors
/// or put them back.
/// </summary>
/// <remarks>
/// The crosshair is not a window drawn over the game — it is baked into Roblox's
/// own cursor pictures by <see cref="RobloxCursors"/>, so it is the real cursor
/// you aim with, first person included. The same <see cref="CrosshairImage"/>
/// that renders the cursor files renders the gallery tiles and the preview, so
/// the one you pick is the one you get.
/// </remarks>
public partial class MainWindow
{
    /// <summary>The tile that means "Roblox's own cursor, nothing of ours".</summary>
    private const string DefaultCrosshair = "Default";

    private readonly RobloxCursors _cursors = new();

    private string _crosshairName = DefaultCrosshair;
    private List<CustomCrosshair> _customCrosshairs = new();
    private Dictionary<string, int> _crosshairSizes = new();

    private CrosshairShape _builderShape = CrosshairShape.Cross;
    private string _builderColor = "#33FF66";
    private string? _builderDotColor;

    /// <summary>Stops the slider's own change event saving while settings load.</summary>
    private bool _crosshairLoading;

    private void WireCrosshair()
    {
        CrosshairSizeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty || _crosshairLoading) return;

            _crosshairSizes[_crosshairName] = (int)CrosshairSizeSlider.Value;
            RefreshCrosshairPreview();
            SaveCrosshairSettings();
        };

        CrosshairApplyButton.Click += (_, _) => ApplyCrosshair();
        CrosshairRemoveButton.Click += (_, _) => RemoveCrosshair();
        CrosshairImportButton.Click += async (_, _) => await ImportCrosshairAsync();
        CrosshairAddButton.Click += (_, _) => AddBuiltCrosshair();

        foreach (RadioButton shape in CrosshairShapePanel.Children.OfType<RadioButton>())
        {
            shape.IsCheckedChanged += (sender, _) =>
            {
                if (sender is not RadioButton { IsChecked: true, Tag: string tag }) return;
                if (!Enum.TryParse(tag, out CrosshairShape parsed)) return;

                _builderShape = parsed;
                RefreshBuilderPreview();
            };
        }

        BuildColourSwatches();
    }

    /// <summary>Puts the saved crosshair, sizes and custom ones back on screen.</summary>
    private void LoadCrosshair(AppSettings s)
    {
        _crosshairLoading = true;

        try
        {
            _customCrosshairs = s.CustomCrosshairs ?? new List<CustomCrosshair>();
            _crosshairSizes = s.CrosshairSizes ?? new Dictionary<string, int>();
            _crosshairName = KnownCrosshair(s.CrosshairName) ? s.CrosshairName : DefaultCrosshair;

            CrosshairSizeSlider.Value = SizeFor(_crosshairName);
        }
        finally
        {
            _crosshairLoading = false;
        }

        RefreshCrosshairGallery();
        RefreshCrosshairPreview();
        ReportCrosshairState();
    }

    private bool KnownCrosshair(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && (name == DefaultCrosshair
            || CrosshairGallery.All.Any(e => e.Name == name)
            || _customCrosshairs.Any(c => c.Name == name));

    private int SizeFor(string name) =>
        _crosshairSizes.TryGetValue(name, out int percent) ? Math.Clamp(percent, 40, 160) : 100;

    private double SizeFactor => SizeFor(_crosshairName) / 100.0;

    /// <summary>The chosen crosshair's style, or null when it is an imported picture.</summary>
    private CrosshairStyle? StyleFor(string name)
    {
        // Default is the absence of a crosshair, not one of them: it must not
        // fall through to ByName, which answers with the first gallery entry
        // for anything it does not recognise.
        if (name == DefaultCrosshair) return null;

        CustomCrosshair? custom = _customCrosshairs.FirstOrDefault(c => c.Name == name);
        if (custom != null) return custom.IsImage ? null : custom.ToStyle();

        return CrosshairGallery.ByName(name).Style;
    }

    private string? ImageFileFor(string name) =>
        _customCrosshairs.FirstOrDefault(c => c.Name == name)?.ImageFile;

    // ---- applying ----

    /// <summary>
    /// Writes the chosen crosshair over Roblox's cursors, or restores them when
    /// Default is chosen.
    /// </summary>
    private void ApplyCrosshair()
    {
        if (_crosshairName == DefaultCrosshair)
        {
            // Default is not a crosshair to write, it is the absence of one, so
            // applying it means putting Roblox's own cursors back.
            RemoveCrosshair();
            return;
        }

        if (_cursors.Discover().Count == 0)
        {
            CrosshairStatus.Text = "No Roblox install found to apply to.";
            return;
        }

        string? imageFile = ImageFileFor(_crosshairName);
        CrosshairStyle style = StyleFor(_crosshairName) ?? new CrosshairStyle();
        double factor = SizeFactor;

        CursorApplyResult result = _cursors.Apply(pixels => imageFile != null
            ? CrosshairImage.RenderImageCursorPng(CrosshairStore.Resolve(imageFile), pixels, factor)
            : CrosshairImage.RenderCursorPng(style, pixels, factor));

        CrosshairStatus.Text = result.AnyWritten
            ? $"Applied to {result.Written} cursor file(s) — quit Roblox and open it again."
              + (result.Failed > 0 ? $" {result.Failed} could not be written; close Roblox and try again." : "")
            : "Could not write Roblox's cursor files. Close Roblox and try again.";
    }

    private void RemoveCrosshair()
    {
        int restored = _cursors.Remove();

        CrosshairStatus.Text = restored > 0
            ? $"Removed — {restored} cursor file(s) back to normal. Quit Roblox and open it again."
            : "Nothing to remove; Roblox's own cursors are in place.";
    }

    private void ReportCrosshairState() =>
        CrosshairStatus.Text = _cursors.Discover().Count == 0
            ? "No Roblox install found. Install Roblox, then come back."
            : _cursors.IsApplied()
                ? "A crosshair is applied. Pick another and Apply, or Remove to go back to normal."
                : "Pick a crosshair below, then Apply to Roblox.";

    // ---- the gallery ----

    private void RefreshCrosshairGallery()
    {
        CrosshairGalleryPanel.Children.Clear();

        CrosshairGalleryPanel.Children.Add(BuildCrosshairTile(DefaultCrosshair, null, null, custom: false));

        foreach ((string name, CrosshairStyle style) in CrosshairGallery.All)
            CrosshairGalleryPanel.Children.Add(BuildCrosshairTile(name, style, null, custom: false));

        foreach (CustomCrosshair custom in _customCrosshairs)
        {
            CrosshairGalleryPanel.Children.Add(BuildCrosshairTile(
                custom.Name,
                custom.IsImage ? null : custom.ToStyle(),
                custom.ImageFile,
                custom: true));
        }
    }

    /// <summary>One gallery tile: the crosshair drawn, its name, and a ✕ if it is ours.</summary>
    private Control BuildCrosshairTile(string name, CrosshairStyle? style, string? imageFile, bool custom)
    {
        var picture = new Image { Width = 44, Height = 44, Stretch = Stretch.Uniform };

        if (style != null) picture.Source = CrosshairImage.RenderBitmap(style, 64, 1.0);
        else if (imageFile != null) picture.Source = CrosshairImage.RenderImageBitmap(CrosshairStore.Resolve(imageFile), 64, 1.0);
        else picture.Source = CrosshairImage.RenderBitmap(new CrosshairStyle(CrosshairShape.Dot, DotSize: 2), 64, 0.5);

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        stack.Children.Add(picture);
        stack.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 10,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var tile = new Button
        {
            Content = stack,
            Width = 86,
            Height = 86,
            Margin = new Avalonia.Thickness(0, 0, 8, 8),
            Padding = new Avalonia.Thickness(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        if (name == _crosshairName) tile.BorderBrush = Brushes.White;
        tile.BorderThickness = new Avalonia.Thickness(name == _crosshairName ? 2 : 1);

        tile.Click += (_, _) => SelectCrosshair(name);

        if (!custom) return tile;

        // A custom tile carries its own delete, since nothing else would ever
        // remove it.
        var remove = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Avalonia.Thickness(4, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        remove.Click += (_, _) => DeleteCustomCrosshair(name);

        var holder = new Panel();
        holder.Children.Add(tile);
        holder.Children.Add(remove);
        return holder;
    }

    private void SelectCrosshair(string name)
    {
        _crosshairName = name;

        _crosshairLoading = true;
        try { CrosshairSizeSlider.Value = SizeFor(name); }
        finally { _crosshairLoading = false; }

        // The size control means nothing for Default: there is no crosshair to
        // make bigger, only Roblox's own cursor to put back.
        CrosshairSizeRow.IsVisible = name != DefaultCrosshair;

        RefreshCrosshairGallery();
        RefreshCrosshairPreview();

        CrosshairStatus.Text = name == DefaultCrosshair
            ? "Default — Apply puts Roblox's own cursor back."
            : $"✓ Switched to {name}. Apply to Roblox when you are ready.";

        SaveCrosshairSettings();
    }

    private void RefreshCrosshairPreview()
    {
        string? imageFile = ImageFileFor(_crosshairName);
        CrosshairStyle? style = StyleFor(_crosshairName);

        CrosshairPreviewImage.Source = imageFile != null
            ? CrosshairImage.RenderImageBitmap(CrosshairStore.Resolve(imageFile), 132, 1.0)
            : CrosshairImage.RenderBitmap(style ?? new CrosshairStyle(CrosshairShape.Dot, DotSize: 2),
                132, _crosshairName == DefaultCrosshair ? 0.5 : SizeFactor);

        CrosshairSizeValue.Text = $"{SizeFor(_crosshairName)}%";
    }

    // ---- building your own ----

    private void BuildColourSwatches()
    {
        foreach ((string name, string hex) in CrosshairGallery.Palette)
        {
            CrosshairColorPanel.Children.Add(Swatch(hex, name, main: true));
            CrosshairDotColorPanel.Children.Add(Swatch(hex, name, main: false));
        }

        // "None" means the dot shares the arm colour, which is how a plain
        // one-colour crosshair is built.
        var none = new Button
        {
            Content = "None",
            FontSize = 10,
            Margin = new Avalonia.Thickness(0, 0, 6, 6),
            Padding = new Avalonia.Thickness(8, 4)
        };
        none.Click += (_, _) => { _builderDotColor = null; RefreshBuilderPreview(); };
        CrosshairDotColorPanel.Children.Add(none);
    }

    private Button Swatch(string hex, string name, bool main)
    {
        var swatch = new Button
        {
            Width = 26,
            Height = 26,
            Margin = new Avalonia.Thickness(0, 0, 6, 6),
            Background = new SolidColorBrush(Color.Parse(hex)),
            [ToolTip.TipProperty] = name
        };

        swatch.Click += (_, _) =>
        {
            if (main) _builderColor = hex;
            else _builderDotColor = hex;

            RefreshBuilderPreview();
        };

        return swatch;
    }

    private CrosshairStyle BuilderStyle() => new(
        _builderShape, Size: 9, Thickness: 3, Gap: 4, DotSize: 3,
        ColorHex: _builderColor, DotColorHex: _builderDotColor);

    private void RefreshBuilderPreview() =>
        CrosshairBuilderPreview.Source = CrosshairImage.RenderBitmap(BuilderStyle(), 88, 1.0);

    private void AddBuiltCrosshair()
    {
        CustomCrosshair made = new()
        {
            Name = NextCustomName("Custom"),
            Shape = _builderShape.ToString(),
            Color = _builderColor,
            DotColor = _builderDotColor
        };

        _customCrosshairs.Add(made);
        SaveCrosshairSettings();
        SelectCrosshair(made.Name);
    }

    /// <summary>
    /// The first free "Custom N" or "Import N".
    /// </summary>
    /// <remarks>
    /// Names key the per-crosshair size map, so two tiles sharing a name would
    /// share a size. Counting from the first free number rather than the count
    /// means deleting one and adding another cannot collide.
    /// </remarks>
    private string NextCustomName(string prefix)
    {
        for (int n = 1; ; n++)
        {
            string candidate = $"{prefix} {n}";
            if (_customCrosshairs.All(c => c.Name != candidate)) return candidate;
        }
    }

    private async Task ImportCrosshairAsync()
    {
        try
        {
            IReadOnlyList<IStorageFile> picked = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Choose a crosshair picture",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { CrosshairImageFileType }
                });

            string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
            if (path == null) return;

            string? stored = CrosshairStore.Store(path);
            if (stored == null)
            {
                CrosshairStatus.Text = "That file could not be read. Try a PNG or JPEG.";
                return;
            }

            CustomCrosshair imported = new()
            {
                Name = NextCustomName("Import"),
                ImageFile = stored
            };

            _customCrosshairs.Add(imported);
            SaveCrosshairSettings();
            SelectCrosshair(imported.Name);
        }
        catch
        {
            CrosshairStatus.Text = "That picture could not be imported.";
        }
    }

    private static readonly FilePickerFileType CrosshairImageFileType = new("Pictures")
    {
        Patterns = CrosshairStore.Allowed.Select(e => "*" + e).ToArray()
    };

    /// <summary>Writes the crosshair choices into the settings file.</summary>
    private void SaveCrosshairSettings()
    {
        _settings.CrosshairName = _crosshairName;
        _settings.CrosshairSizes = _crosshairSizes;
        _settings.CustomCrosshairs = _customCrosshairs;
        _settings.Save();
    }

    private void DeleteCustomCrosshair(string name)
    {
        CustomCrosshair? custom = _customCrosshairs.FirstOrDefault(c => c.Name == name);
        if (custom == null) return;

        // The stored picture goes with it, or the folder fills up with images no
        // tile refers to any more.
        if (custom.IsImage) CrosshairStore.Delete(custom.ImageFile);

        _customCrosshairs.Remove(custom);
        _crosshairSizes.Remove(name);

        // Deleting the one in use leaves nothing selected, so fall back to
        // Default. Roblox's cursors are untouched either way — Remove is what
        // restores them, and the backups are still there.
        if (_crosshairName == name)
        {
            SelectCrosshair(DefaultCrosshair);
            return;
        }

        SaveCrosshairSettings();
        RefreshCrosshairGallery();
    }
}
