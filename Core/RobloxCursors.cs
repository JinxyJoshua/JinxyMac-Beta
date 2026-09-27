using System;
using System.Collections.Generic;
using System.IO;

namespace JinxyMac.Core;

/// <summary>One cursor image a crosshair can be written over.</summary>
/// <param name="Path">The PNG on disk.</param>
/// <param name="Pixels">Its side in pixels, read from the file itself.</param>
public readonly record struct CursorTarget(string Path, int Pixels);

/// <summary>The tally from applying a crosshair.</summary>
public readonly record struct CursorApplyResult(int Written, int Failed)
{
    public bool AnyWritten => Written > 0;
}

/// <summary>
/// Writes a crosshair into Roblox's own cursor pictures, and puts the originals
/// back.
/// </summary>
/// <remarks>
/// Roblox in first person shows only a faint dot, so replacing the cursor image
/// is how a real crosshair gets on screen: it becomes the actual cursor, drawn
/// exactly where the game reads the mouse.
///
/// <para><b>How macOS differs from Windows.</b> On Windows the cursors live in a
/// per-version folder under LocalAppData, and bootstrappers (Bloxstrap and the
/// rest) have Modifications folders worth writing too. On macOS the same content
/// tree is inside the application bundle — <c>Roblox.app/Contents/Resources/
/// content/textures/…</c> — there is one of it rather than one per version, and
/// there are no bootstrappers, so that whole half of the Windows code is gone
/// rather than ported.</para>
///
/// <para>Editing a file inside a signed app bundle was the open question, since
/// macOS can refuse to launch an app whose contents changed. It was tested by
/// hand before this was written: a replaced cursor shows in game and survives a
/// relaunch.</para>
///
/// <para>Reversible by keeping a <c>.jinxybak</c> copy of each original the first
/// time it is replaced, so <see cref="Remove"/> restores the untouched file even
/// after the crosshair has been changed several times.</para>
///
/// <para>Roblox reads the cursor at launch, so a change lands on the next
/// relaunch. A Roblox update replaces the bundle with fresh cursors; re-applying
/// covers it.</para>
///
/// <para>The drawing is injected rather than referenced, so the file logic can be
/// tested without a screen — and the search roots are injected so it can be
/// tested without Roblox.</para>
/// </remarks>
public sealed class RobloxCursors
{
    private const string BackupSuffix = ".jinxybak";

    // The three cursors worth replacing. IBeamCursor is the text caret and is
    // left alone so typing in chat still looks normal.
    private const string ArrowCursor = "ArrowCursor.png";
    private const string ArrowFarCursor = "ArrowFarCursor.png";
    private const string MouseLockedCursor = "MouseLockedCursor.png";

    /// <summary>
    /// What the arrows and the locked cursor are assumed to be when their size
    /// cannot be read.
    /// </summary>
    /// <remarks>
    /// The Windows files are 64 and 32 and the Mac ones appear to match, but a
    /// guess is only the fallback: <see cref="Discover"/> reads the real size out
    /// of each PNG, because Roblox draws the cursor at the image's own size and a
    /// wrong guess is a wrong-sized crosshair.
    /// </remarks>
    private const int UsualArrowPixels = 64;
    private const int UsualLockedPixels = 32;

    private readonly IReadOnlyList<string> _searchRoots;

    /// <param name="searchRoots">
    /// Folders that may contain <c>Roblox.app</c>. Defaults to the two places a
    /// Mac install lands: <c>/Applications</c> and <c>~/Applications</c>.
    /// </param>
    public RobloxCursors(IEnumerable<string>? searchRoots = null)
    {
        _searchRoots = searchRoots is null ? DefaultRoots() : new List<string>(searchRoots);
    }

    private static List<string> DefaultRoots()
    {
        var roots = new List<string> { "/Applications" };

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)) roots.Add(Path.Combine(home, "Applications"));

        return roots;
    }

    /// <summary>Every cursor file a crosshair would be written over right now.</summary>
    public IReadOnlyList<CursorTarget> Discover()
    {
        var targets = new List<CursorTarget>();

        foreach (string root in _searchRoots)
        {
            string textures = Path.Combine(
                root, "Roblox.app", "Contents", "Resources", "content", "textures");

            if (!Directory.Exists(textures)) continue;

            string km = Path.Combine(textures, "Cursors", "KeyboardMouse");

            AddIfFile(targets, Path.Combine(km, ArrowCursor), UsualArrowPixels);
            AddIfFile(targets, Path.Combine(km, ArrowFarCursor), UsualArrowPixels);
            AddIfFile(targets, Path.Combine(textures, MouseLockedCursor), UsualLockedPixels);
        }

        return targets;
    }

    private static void AddIfFile(List<CursorTarget> targets, string path, int fallbackPixels)
    {
        if (File.Exists(path)) targets.Add(new CursorTarget(path, PngWidth(path) ?? fallbackPixels));
    }

    /// <summary>
    /// The width recorded in a PNG's header, or null if the file is not a PNG.
    /// </summary>
    /// <remarks>
    /// Reading eight bytes of header rather than decoding the image: this runs in
    /// Core, which has no image library and must not gain one, and the width is
    /// all that is wanted.
    /// </remarks>
    internal static int? PngWidth(string path)
    {
        try
        {
            using FileStream file = File.OpenRead(path);

            Span<byte> head = stackalloc byte[24];
            if (file.Read(head) != head.Length) return null;

            // Signature, then a length-and-type pair, then IHDR's width.
            ReadOnlySpan<byte> signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            if (!head[..8].SequenceEqual(signature)) return null;
            if (!head[12..16].SequenceEqual("IHDR"u8)) return null;

            int width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];

            return width > 0 ? width : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Whether a crosshair is currently written anywhere.</summary>
    public bool IsApplied()
    {
        foreach (CursorTarget t in Discover())
            if (File.Exists(t.Path + BackupSuffix)) return true;

        return false;
    }

    /// <summary>
    /// Writes <paramref name="pngForPixels"/>'s image over every cursor, backing
    /// up each original the first time.
    /// </summary>
    /// <param name="pngForPixels">
    /// Gives the PNG bytes for a required pixel size. Called once per distinct
    /// size and cached, so the drawing happens twice rather than once per file.
    /// </param>
    public CursorApplyResult Apply(Func<int, byte[]> pngForPixels)
    {
        var cache = new Dictionary<int, byte[]>();
        byte[] Bytes(int px) => cache.TryGetValue(px, out byte[]? b) ? b : cache[px] = pngForPixels(px);

        int written = 0, failed = 0;

        foreach (CursorTarget t in Discover())
        {
            try
            {
                // Before the file is touched: a backup only if there is not one
                // already, so switching crosshairs many times never buries the
                // original under a previous crosshair.
                string backup = t.Path + BackupSuffix;
                if (!File.Exists(backup) && File.Exists(t.Path)) File.Copy(t.Path, backup);

                File.WriteAllBytes(t.Path, Bytes(t.Pixels));
                written++;
            }
            catch
            {
                // A single locked or unwritable file should not sink the rest —
                // count it and move on. Roblox running is the usual cause, and it
                // is why applying asks for a relaunch.
                failed++;
            }
        }

        return new CursorApplyResult(written, failed);
    }

    /// <summary>Puts every original back.</summary>
    public int Remove()
    {
        int restored = 0;

        foreach (CursorTarget t in Discover())
        {
            try
            {
                string backup = t.Path + BackupSuffix;
                if (!File.Exists(backup)) continue;

                File.Copy(backup, t.Path, overwrite: true);
                File.Delete(backup);
                restored++;
            }
            catch
            {
                // Leave it for the next Remove rather than throwing half way.
            }
        }

        return restored;
    }
}
