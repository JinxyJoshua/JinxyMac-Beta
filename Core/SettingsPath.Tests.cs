using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// The atomic-write helper every settings store in this app is meant to
/// funnel through. Pinned directly here, once, rather than only through each
/// store's own Save() — every one of AppSettings, MacroStore, KitWheelStore,
/// PresetStore and ClickHistory relies on the exact same contract, and a
/// regression in the shared helper would otherwise have to be caught six
/// separate times.
/// </summary>
public class SettingsPathTests
{
    private static string TestFilePath =>
        SettingsPath.For($"settingspath-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void WritesTheContentSoItCanBeReadBack()
    {
        string path = TestFilePath;

        try
        {
            SettingsPath.WriteAtomic(path, "hello");

            Assert.Equal("hello", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void OverwritesWhateverWasThereBefore()
    {
        string path = TestFilePath;

        try
        {
            File.WriteAllText(path, "old content, much longer than the new one");

            SettingsPath.WriteAtomic(path, "new");

            Assert.Equal("new", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// The whole point of routing every store's Save() through this helper
    /// instead of a bare File.WriteAllText: a write that fails partway must
    /// leave the previous file exactly as it was, never truncated and never
    /// deleted. File.WriteAllText fails this by construction — it truncates
    /// the target before writing a single byte of the new content, so a kill
    /// mid-write (force-quitting an unresponsive app, or a power cut) leaves
    /// the file zero-length or half-written. Failure is forced here the same
    /// way Wallpaper.Tests.cs forces it for File.Copy: by putting a directory
    /// where the temp file needs to land, so the write throws before the
    /// real file is ever opened for writing.
    /// </summary>
    [Fact]
    public void AWriteThatFailsPartwayLeavesThePreviousFileIntact()
    {
        string path = TestFilePath;
        string temp = path + ".tmp";

        try
        {
            File.WriteAllText(path, "the original content");

            Directory.CreateDirectory(temp);

            Assert.ThrowsAny<Exception>(() => SettingsPath.WriteAtomic(path, "the new content"));

            Assert.Equal("the original content", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// A failed write must not leave the temp file behind either — a stray
    /// "<file>.json.tmp" that nobody cleans up is not what "left intact"
    /// means.
    /// </summary>
    [Fact]
    public void AWriteThatFailsPartwayCleansUpItsOwnTempFile()
    {
        string path = TestFilePath;
        string temp = path + ".tmp";

        // This time the failure comes from the move rather than the initial
        // write, so a real temp file is created and has to be cleaned up
        // rather than merely refused: block the destination instead of the
        // temp name.
        try
        {
            Directory.CreateDirectory(path);

            Assert.ThrowsAny<Exception>(() => SettingsPath.WriteAtomic(path, "content"));

            Assert.False(File.Exists(temp));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>A write with nothing already there just creates the file.</summary>
    [Fact]
    public void AFreshFileIsCreatedWhenNothingWasThereBefore()
    {
        string path = TestFilePath;

        try
        {
            Assert.False(File.Exists(path));

            SettingsPath.WriteAtomic(path, "first write");

            Assert.Equal("first write", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
