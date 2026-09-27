using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Writing a crosshair into Roblox's cursor files, and putting them back.
/// </summary>
/// <remarks>
/// The file handling is the part that can destroy something a user cares about —
/// Roblox's own files — so it is tested against a fake Roblox bundle in a temp
/// folder rather than a real install. Nothing here needs a Mac, a screen, or
/// Roblox.
/// </remarks>
public class RobloxCursorsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "JinxyCursorTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>A Roblox.app with the three cursor files in it.</summary>
    private string MakeRobloxBundle(int arrowPixels = 64, int lockedPixels = 32)
    {
        string bundle = Path.Combine(_root, "Applications", "Roblox.app");
        string textures = Path.Combine(bundle, "Contents", "Resources", "content", "textures");
        string km = Path.Combine(textures, "Cursors", "KeyboardMouse");

        Directory.CreateDirectory(km);
        File.WriteAllBytes(Path.Combine(km, "ArrowCursor.png"), Png(arrowPixels));
        File.WriteAllBytes(Path.Combine(km, "ArrowFarCursor.png"), Png(arrowPixels));
        File.WriteAllBytes(Path.Combine(textures, "MouseLockedCursor.png"), Png(lockedPixels));

        return Path.Combine(_root, "Applications");
    }

    /// <summary>Enough of a PNG for the size to be read out of it.</summary>
    private static byte[] Png(int side)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BitConverter.GetBytes(13).Reverse().ToArray().CopyTo(bytes, 8);
        "IHDR"u8.ToArray().CopyTo(bytes, 12);
        BitConverter.GetBytes(side).Reverse().ToArray().CopyTo(bytes, 16);
        BitConverter.GetBytes(side).Reverse().ToArray().CopyTo(bytes, 20);
        return bytes;
    }

    private RobloxCursors Cursors(string apps) => new(new[] { apps });

    // ---- finding the files ----

    [Fact]
    public void FindsTheThreeCursorsInARobloxBundle()
    {
        var cursors = Cursors(MakeRobloxBundle());

        IReadOnlyList<CursorTarget> found = cursors.Discover();

        Assert.Equal(3, found.Count);
        Assert.Contains(found, t => t.Path.EndsWith("ArrowCursor.png", StringComparison.Ordinal));
        Assert.Contains(found, t => t.Path.EndsWith("ArrowFarCursor.png", StringComparison.Ordinal));
        Assert.Contains(found, t => t.Path.EndsWith("MouseLockedCursor.png", StringComparison.Ordinal));
    }

    /// <summary>
    /// The text caret. Replacing it makes typing in chat look wrong, so it is
    /// never touched even though it sits in the same folder.
    /// </summary>
    [Fact]
    public void LeavesTheTextCaretAlone()
    {
        string apps = MakeRobloxBundle();
        string caret = Path.Combine(apps, "Roblox.app", "Contents", "Resources",
            "content", "textures", "Cursors", "KeyboardMouse", "IBeamCursor.png");
        File.WriteAllBytes(caret, Png(64));

        Assert.DoesNotContain(Cursors(apps).Discover(),
            t => t.Path.EndsWith("IBeamCursor.png", StringComparison.Ordinal));
    }

    [Fact]
    public void FindsNothingWhenRobloxIsNotInstalled()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Applications"));

        Assert.Empty(Cursors(Path.Combine(_root, "Applications")).Discover());
    }

    /// <summary>
    /// The size comes out of the file rather than being assumed, because the Mac
    /// files were never confirmed to be the 64 and 32 the Windows ones are. A
    /// cursor written at the wrong size is drawn at the wrong size by Roblox.
    /// </summary>
    [Fact]
    public void ReadsEachCursorsSizeOutOfThePngItself()
    {
        var cursors = Cursors(MakeRobloxBundle(arrowPixels: 128, lockedPixels: 48));

        IReadOnlyList<CursorTarget> found = cursors.Discover();

        Assert.Equal(128, found.First(t => t.Path.EndsWith("ArrowCursor.png", StringComparison.Ordinal)).Pixels);
        Assert.Equal(48, found.First(t => t.Path.EndsWith("MouseLockedCursor.png", StringComparison.Ordinal)).Pixels);
    }

    [Fact]
    public void FallsBackToTheUsualSizeWhenThePngCannotBeRead()
    {
        string apps = MakeRobloxBundle();
        File.WriteAllText(Path.Combine(apps, "Roblox.app", "Contents", "Resources",
            "content", "textures", "Cursors", "KeyboardMouse", "ArrowCursor.png"), "not a png");

        Assert.Equal(64, Cursors(apps).Discover()
            .First(t => t.Path.EndsWith("ArrowCursor.png", StringComparison.Ordinal)).Pixels);
    }

    // ---- applying, and staying reversible ----

    [Fact]
    public void ApplyWritesEveryCursorAndBacksUpTheOriginals()
    {
        string apps = MakeRobloxBundle();
        var cursors = Cursors(apps);

        CursorApplyResult result = cursors.Apply(_ => new byte[] { 1, 2, 3 });

        Assert.Equal(3, result.Written);
        Assert.Equal(0, result.Failed);

        foreach (CursorTarget t in cursors.Discover())
        {
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(t.Path));
            Assert.True(File.Exists(t.Path + ".jinxybak"), $"no backup beside {t.Path}");
        }
    }

    /// <summary>
    /// The one that matters. Switching crosshairs applies again over an already
    /// replaced file, and the backup must still hold Roblox's original rather
    /// than the previous crosshair.
    /// </summary>
    [Fact]
    public void ApplyingTwiceKeepsTheTrueOriginalAsTheBackup()
    {
        string apps = MakeRobloxBundle();
        var cursors = Cursors(apps);
        string arrow = cursors.Discover()
            .First(t => t.Path.EndsWith("ArrowCursor.png", StringComparison.Ordinal)).Path;
        byte[] original = File.ReadAllBytes(arrow);

        cursors.Apply(_ => new byte[] { 1 });
        cursors.Apply(_ => new byte[] { 2 });

        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(arrow));
        Assert.Equal(original, File.ReadAllBytes(arrow + ".jinxybak"));
    }

    [Fact]
    public void RemovePutsEveryOriginalBackAndClearsTheBackups()
    {
        string apps = MakeRobloxBundle();
        var cursors = Cursors(apps);
        Dictionary<string, byte[]> before = cursors.Discover()
            .ToDictionary(t => t.Path, t => File.ReadAllBytes(t.Path));

        cursors.Apply(_ => new byte[] { 9 });
        int restored = cursors.Remove();

        Assert.Equal(3, restored);
        foreach ((string path, byte[] original) in before)
        {
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".jinxybak"), $"backup left beside {path}");
        }
    }

    [Fact]
    public void RemoveWithNothingAppliedChangesNothing()
    {
        string apps = MakeRobloxBundle();
        var cursors = Cursors(apps);
        byte[] before = File.ReadAllBytes(cursors.Discover()[0].Path);

        Assert.Equal(0, cursors.Remove());
        Assert.Equal(before, File.ReadAllBytes(cursors.Discover()[0].Path));
    }

    [Fact]
    public void IsAppliedFollowsTheBackups()
    {
        string apps = MakeRobloxBundle();
        var cursors = Cursors(apps);

        Assert.False(cursors.IsApplied());
        cursors.Apply(_ => new byte[] { 1 });
        Assert.True(cursors.IsApplied());
        cursors.Remove();
        Assert.False(cursors.IsApplied());
    }

    /// <summary>
    /// Drawing is the expensive part, and the arrows share a size. The renderer
    /// should be asked once per distinct size, not once per file.
    /// </summary>
    [Fact]
    public void DrawsOncePerDistinctSizeRatherThanOncePerFile()
    {
        var asked = new List<int>();
        Cursors(MakeRobloxBundle()).Apply(px => { asked.Add(px); return new byte[] { 0 }; });

        Assert.Equal(new[] { 64, 32 }, asked.OrderByDescending(x => x).ToArray());
        Assert.Equal(2, asked.Count);
    }

    /// <summary>
    /// A Roblox that is running, or a bundle the user cannot write to, must not
    /// stop the other files being written. The count is what the status line
    /// reports.
    /// </summary>
    [Fact]
    public void CountsAFileItCannotWriteInsteadOfThrowing()
    {
        string apps = MakeRobloxBundle();
        var cursors = Cursors(apps);
        string arrow = cursors.Discover()
            .First(t => t.Path.EndsWith("ArrowCursor.png", StringComparison.Ordinal)).Path;

        // Read-only rather than an open handle with FileShare.None: a share lock
        // stops a write on Windows and does nothing on macOS, where locking is
        // advisory — so that version of this test would pass here and prove
        // nothing on the machine the code actually ships to. The read-only
        // attribute maps to the write permission bits on Unix.
        File.SetAttributes(arrow, FileAttributes.ReadOnly);

        try
        {
            CursorApplyResult result = cursors.Apply(_ => new byte[] { 7 });

            Assert.Equal(1, result.Failed);
            Assert.Equal(2, result.Written);
            Assert.True(result.AnyWritten);
        }
        finally
        {
            File.SetAttributes(arrow, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// Roblox in ~/Applications rather than /Applications is a normal install,
    /// and both are searched.
    /// </summary>
    [Fact]
    public void SearchesEveryPlaceRobloxCanBeInstalled()
    {
        string apps = MakeRobloxBundle();
        string second = Path.Combine(_root, "HomeApplications");
        Directory.CreateDirectory(second);

        Assert.Equal(3, new RobloxCursors(new[] { apps, second }).Discover().Count);
    }
}
