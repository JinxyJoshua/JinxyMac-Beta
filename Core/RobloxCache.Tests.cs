using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Measuring and emptying the cache folders.
/// </summary>
/// <remarks>
/// Against a real temporary directory rather than an abstraction over the file
/// system. What is worth checking here is the behaviour around files that
/// vanish, folders that nest and paths that do not exist — none of which a mock
/// would reproduce faithfully, and all of which happen every time this runs
/// against a Roblox that is currently open.
/// </remarks>
public class RobloxCacheTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "JinxyMacCacheTests", Guid.NewGuid().ToString("N"));

    public RobloxCacheTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* already gone */ }
    }

    private string Make(string relative, int bytes)
    {
        string path = Path.Combine(_root, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);

        return path;
    }

    private CacheFolder Folder(string name = "cache") => new("Test", Path.Combine(_root, name));

    // ---- measuring ----

    [Fact]
    public void AddsUpEverythingUnderTheFolder()
    {
        Make("cache/a.bin", 1000);
        Make("cache/nested/deep/b.bin", 2000);

        CacheReport report = RobloxCache.Measure(new[] { Folder() });

        Assert.Equal(3000, report.Bytes);
        Assert.Equal(2, report.Files);
        Assert.Equal(1, report.Folders);
    }

    /// <summary>
    /// A folder that is not there is the normal case on a machine where Roblox
    /// Studio was never installed. It must not count and must not throw.
    /// </summary>
    [Fact]
    public void SkipsFoldersThatDoNotExist()
    {
        CacheReport report = RobloxCache.Measure(new[] { new CacheFolder("Absent", Path.Combine(_root, "nope")) });

        Assert.Equal(0, report.Bytes);
        Assert.Equal(0, report.Folders);
    }

    [Fact]
    public void MeasuresNothingAsNothing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "cache"));

        CacheReport report = RobloxCache.Measure(new[] { Folder() });

        Assert.Equal(0, report.Bytes);
        Assert.Equal(1, report.Folders);
    }

    // ---- clearing ----

    [Fact]
    public void RemovesTheFilesAndReportsWhatItFreed()
    {
        Make("cache/a.bin", 500);
        Make("cache/nested/b.bin", 1500);

        CacheReport report = RobloxCache.Clear(new[] { Folder() });

        Assert.Equal(2000, report.Bytes);
        Assert.Equal(2, report.Files);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "cache"), "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// The folder itself survives. Roblox holds a handle on the directory while
    /// it runs, and deleting it outright fails where emptying it succeeds.
    /// </summary>
    [Fact]
    public void LeavesTheFolderItselfInPlace()
    {
        Make("cache/a.bin", 10);

        RobloxCache.Clear(new[] { Folder() });

        Assert.True(Directory.Exists(Path.Combine(_root, "cache")));
    }

    [Fact]
    public void RemovesTheEmptiedSubfolders()
    {
        Make("cache/one/two/three/a.bin", 10);

        RobloxCache.Clear(new[] { Folder() });

        Assert.False(Directory.Exists(Path.Combine(_root, "cache", "one")));
    }

    /// <summary>
    /// The ordering bug this replaced: sorting by path length puts a
    /// long-named parent ahead of its short-named child, so the parent is tried
    /// while it still has contents and is left behind.
    /// </summary>
    [Fact]
    public void DeletesChildrenBeforeTheirParents()
    {
        Directory.CreateDirectory(Path.Combine(_root, "cache", "a-very-long-folder-name-indeed", "x"));

        string[] order = RobloxCache.SubfoldersDeepestFirst(Path.Combine(_root, "cache")).ToArray();

        Assert.Equal(2, order.Length);
        Assert.True(RobloxCache.Depth(order[0]) > RobloxCache.Depth(order[1]));
    }

    /// <summary>
    /// The bug this replaced: <c>Directory.EnumerateFiles</c> is lazy, so a
    /// bare <c>try { return Directory.EnumerateFiles(...); }</c> only guards
    /// the call, not the walk — an exception raised while a caller's own
    /// <c>foreach</c> pulls items out would come from that foreach, uncaught,
    /// not from here.
    /// </summary>
    /// <remarks>
    /// The exact race this guards against — a file or folder vanishing while
    /// <c>EnumerateFiles</c> is still recursing through the tree — is not
    /// reproducible deterministically from a single thread without an
    /// abstraction over the file system, which this test file deliberately
    /// does not use (see the class remarks). What is checked instead is the
    /// actual mechanism of the fix: that <c>Walk</c> finishes reading the
    /// whole tree into a concrete array before it returns, rather than handing
    /// back something that still touches disk when the caller enumerates it.
    /// Deleting the folder immediately afterwards proves that — if the walk
    /// were still lazy, enumerating the result below would throw.
    /// </remarks>
    [Fact]
    public void WalkFinishesReadingBeforeItReturns()
    {
        Make("cache/a.bin", 10);
        Make("cache/nested/b.bin", 20);
        string cachePath = Path.Combine(_root, "cache");

        IEnumerable<string> walked = RobloxCache.Walk(cachePath);

        Directory.Delete(cachePath, recursive: true);

        // If Walk had only guarded the call and not the walk, the directory
        // being gone now would make this throw instead of yielding the two
        // files already found.
        Assert.Equal(2, walked.Count());
    }

    /// <summary>
    /// A folder that fails outright — this walks a file, not a directory, so
    /// <c>EnumerateFiles</c> throws on the very first call rather than partway
    /// through, but it exercises the same catch.
    /// </summary>
    [Fact]
    public void WalkReturnsNothingRatherThanThrowingWhenTheRootIsUnusable()
    {
        string notADirectory = Make("not-a-folder.bin", 5);

        IEnumerable<string> walked = RobloxCache.Walk(notADirectory);

        Assert.Empty(walked);
    }

    [Fact]
    public void ClearingNothingIsNotAnError()
    {
        CacheReport report = RobloxCache.Clear(new[] { new CacheFolder("Absent", Path.Combine(_root, "nope")) });

        Assert.Equal(0, report.Bytes);
    }

    // ---- what the page shows ----

    [Theory]
    [InlineData(0, "nothing")]
    [InlineData(2048, "2 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    public void SizesReadAtTheRightScale(long bytes, string expected)
    {
        Assert.Equal(expected, new CacheReport(bytes, 1, 1).SizeText);
    }

    [Fact]
    public void GigabytesGetADecimal()
    {
        Assert.Equal("2.5 GB", new CacheReport((long)(2.5 * 1024 * 1024 * 1024), 1, 1).SizeText);
    }

    /// <summary>
    /// Nothing outside the user's own caches and logs. A path list is the kind
    /// of thing that grows carelessly, and the wrong entry here deletes
    /// somebody's files.
    /// </summary>
    [Fact]
    public void OnlyTouchesFoldersUnderTheUsersHome()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.All(RobloxCache.Folders(), folder =>
            Assert.StartsWith(home, folder.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryFolderIsNamedAndDistinct()
    {
        IReadOnlyList<CacheFolder> folders = RobloxCache.Folders();

        Assert.NotEmpty(folders);
        Assert.All(folders, f => Assert.False(string.IsNullOrWhiteSpace(f.Label)));
        Assert.Equal(folders.Count, folders.Select(f => f.Path).Distinct().Count());
    }
}

/// <summary>Escaping for the AppleScript notification call.</summary>
public class NotifyTests
{
    /// <summary>
    /// A clip name with a quote in it would otherwise close the string early
    /// and hand the rest of the filename to osascript as script.
    /// </summary>
    [Fact]
    public void EscapesQuotes()
    {
        Assert.Equal("clip \\\"one\\\".mp4", Notify.Escape("clip \"one\".mp4"));
    }

    [Fact]
    public void EscapesBackslashesBeforeQuotes()
    {
        Assert.Equal("a\\\\b", Notify.Escape("a\\b"));
    }

    [Fact]
    public void FlattensNewlines()
    {
        Assert.Equal("one two", Notify.Escape("one\ntwo"));
    }

    [Fact]
    public void LeavesOrdinaryTextAlone()
    {
        Assert.Equal("clip-2026-08-23-191327.mp4", Notify.Escape("clip-2026-08-23-191327.mp4"));
    }
}
