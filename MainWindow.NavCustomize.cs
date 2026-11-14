using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using JinxyMac.Core;

namespace JinxyMac;

/// <summary>
/// Hiding, reordering and renaming the sidebar.
/// </summary>
/// <remarks>
/// Fourteen pages is a long sidebar for somebody who uses two of them. This lets
/// the list be cut down to what a person actually opens, put in the order they
/// open it in, and called whatever they call it.
///
/// <para>The arranging itself is <see cref="NavLayout"/>, with no window
/// anywhere near it. This half — moving real buttons about and keeping the
/// editor in step — is the part that cannot be checked from here, so it is kept
/// as small as it will go and does no thinking of its own.</para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>
    /// What each sidebar button is called when nothing has been renamed.
    /// </summary>
    /// <remarks>
    /// Captured once, before any layout is applied. Reading it back off the
    /// button later would read the user's own name for it, and the original — the
    /// thing "reset" restores, and the thing a typed name is compared against —
    /// would be gone.
    /// </remarks>
    private readonly Dictionary<string, string> _navDefaults = new(StringComparer.Ordinal);

    /// <summary>
    /// Every page in the sidebar, in the order the app ships them.
    /// </summary>
    /// <remarks>
    /// Taken from the buttons themselves rather than written out again, so a page
    /// added later cannot be left out of here by being forgotten: it is in the
    /// sidebar, therefore it is in this list.
    /// </remarks>
    private List<NavEntry> NavEntries() =>
        NavList.Children
            .OfType<RadioButton>()
            .Where(b => b.Name is { Length: > 0 })
            .Select(b => new NavEntry(b.Name!, _navDefaults.GetValueOrDefault(b.Name!, b.Name!)))
            .ToList();

    private void WireNavCustomize()
    {
        foreach (RadioButton button in NavList.Children.OfType<RadioButton>())
            if (button.Name is { Length: > 0 } id)
                _navDefaults[id] = button.Content as string ?? id;

        NavResetButton.Click += (_, _) =>
        {
            _settings.NavOrder.Clear();
            _settings.NavHidden.Clear();
            _settings.NavLabels.Clear();
            _settings.Save();

            ApplyNavLayout();
            BuildNavEditor();
        };
    }

    /// <summary>
    /// Puts the sidebar in the saved order, under the saved names, without what
    /// is hidden.
    /// </summary>
    /// <remarks>
    /// The existing buttons are moved rather than rebuilt. Each one was wired to
    /// its page by object at startup, so a freshly built button would arrive
    /// wired to nothing — this is the only version of reordering that cannot
    /// break navigation itself.
    /// </remarks>
    private void ApplyNavLayout()
    {
        List<string> order = NavLayout.Reconcile(_settings.NavOrder, NavEntries());
        var visible = new HashSet<string>(
            NavLayout.Visible(order, _settings.NavHidden), StringComparer.Ordinal);

        Dictionary<string, RadioButton> byId = NavList.Children
            .OfType<RadioButton>()
            .Where(b => b.Name is { Length: > 0 })
            .ToDictionary(b => b.Name!, b => b, StringComparer.Ordinal);

        // Taken out and put back in order, rather than sorted in place:
        // Avalonia's child collection has no move, and emptying it first means
        // the result is exactly the order asked for instead of the outcome of a
        // sequence of swaps.
        NavList.Children.Clear();

        foreach (string id in order)
        {
            if (!byId.TryGetValue(id, out RadioButton? button)) continue;

            button.Content = NavLayout.Label(id, _navDefaults.GetValueOrDefault(id, id),
                _settings.NavLabels);

            button.IsVisible = visible.Contains(id);

            NavList.Children.Add(button);
        }

        // Either somebody hid the page they were on, or the round trip through
        // the collection left nothing ticked. Both leave the content area
        // showing a page the sidebar does not agree with, which reads as the app
        // having lost its place.
        List<RadioButton> buttons = NavList.Children.OfType<RadioButton>().ToList();

        if (buttons.FirstOrDefault(b => b.IsChecked == true) is not { IsVisible: true }
            && buttons.FirstOrDefault(b => b.IsVisible) is { } first)
        {
            first.IsChecked = true;
        }
    }

    /// <summary>
    /// Builds the editor on the Settings page: one row per page.
    /// </summary>
    /// <remarks>
    /// Rebuilt whole on every change rather than patched. The rows carry the
    /// order between them, so one move changes most of them at once, and
    /// rebuilding is both shorter and incapable of leaving a row describing a
    /// page it no longer sits beside.
    /// </remarks>
    private void BuildNavEditor()
    {
        NavEditorList.Children.Clear();

        List<NavEntry> entries = NavEntries();
        List<string> order = NavLayout.Reconcile(_settings.NavOrder, entries);

        Dictionary<string, string> defaults =
            entries.ToDictionary(e => e.Id, e => e.DefaultLabel, StringComparer.Ordinal);

        for (int i = 0; i < order.Count; i++)
            if (defaults.TryGetValue(order[i], out string? fallback))
                NavEditorList.Children.Add(NavEditorRow(order[i], fallback, order, i));
    }

    private Control NavEditorRow(string id, string defaultLabel, List<string> order, int index)
    {
        bool hidden = _settings.NavHidden.Contains(id);

        var show = new CheckBox
        {
            IsChecked = !hidden,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,

            // The last page showing cannot be hidden: an app with no navigation
            // at all could not be put back from inside, since the control that
            // would put it back is itself on a page. Disabled rather than
            // refused on click, so the limit is visible before it is hit.
            IsEnabled = hidden || !NavLayout.IsLastShown(order, _settings.NavHidden, id)
        };

        var name = new TextBox
        {
            Text = NavLayout.Label(id, defaultLabel, _settings.NavLabels),
            Watermark = defaultLabel,
            MaxLength = NavLayout.MaxLabelLength,
            Width = 200,
            Height = 34,
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        show.IsCheckedChanged += (_, _) =>
        {
            if (show.IsChecked == true) _settings.NavHidden.Remove(id);
            else if (!_settings.NavHidden.Contains(id)) _settings.NavHidden.Add(id);

            _settings.Save();

            ApplyNavLayout();

            // The last-one-showing rule may now apply to a different row, so the
            // whole editor is rebuilt rather than this one row.
            BuildNavEditor();
        };

        // On losing focus rather than on each keystroke: saving per character
        // would write the file a dozen times for one word, and would rename the
        // sidebar letter by letter while it was being typed.
        name.LostFocus += (_, _) =>
        {
            string? cleaned = NavLayout.CleanLabel(name.Text, defaultLabel);

            if (cleaned == null) _settings.NavLabels.Remove(id);
            else _settings.NavLabels[id] = cleaned;

            _settings.Save();

            // The tidied version goes back in the box, so what is stored and
            // what is shown cannot disagree.
            name.Text = cleaned ?? defaultLabel;

            ApplyNavLayout();
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            Margin = new Thickness(0, 0, 0, 8)
        };

        Add(show, 0);
        Add(name, 1);
        Add(NavMoveButton("↑", id, -1, index > 0), 2);
        Add(NavMoveButton("↓", id, 1, index < order.Count - 1), 3);

        return row;

        void Add(Control child, int column)
        {
            Grid.SetColumn(child, column);
            row.Children.Add(child);
        }
    }

    private Button NavMoveButton(string glyph, string id, int delta, bool enabled)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 34,
            Height = 34,
            FontSize = 14,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = enabled,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        button.Click += (_, _) =>
        {
            List<string> order = NavLayout.Reconcile(_settings.NavOrder, NavEntries());

            if (!NavLayout.Move(order, id, delta)) return;

            // The whole order is written, not only the part that changed. An
            // order that mentioned just the pages somebody had moved would put
            // everything else back to default on the next launch.
            _settings.NavOrder = order;
            _settings.Save();

            ApplyNavLayout();
            BuildNavEditor();
        };

        return button;
    }
}
