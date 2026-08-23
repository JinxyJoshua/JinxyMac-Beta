namespace JinxyMac.Core;

/// <summary>One folder Roblox fills up and never empties.</summary>
/// <param name="Label">What it holds, in the user's words rather than Apple's.</param>
public sealed record CacheFolder(string Label, string Path)
{
    public bool Exists => Directory.Exists(Path);
}

/// <summary>How much was found, and what came back.</summary>
public readonly record struct CacheReport(long Bytes, int Files, int Folders)
{
    public string SizeText => Bytes switch
    {
        <= 0 => "nothing",
        < 1024 * 1024 => $"{Bytes / 1024.0:0} KB",
        < 1024 * 1024 * 1024 => $"{Bytes / 1024.0 / 1024.0:0} MB",
        _ => $"{Bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB"
    };
}

/// <summary>
/// Roblox's caches and logs, which grow without bound and are never pruned.
/// </summary>
/// <remarks>
/// The Windows build's Tweaks page does not port — registry keys, power plans
/// and network tuning have no macOS equivalent. This is the one piece of it
/// that does, and it is the piece that actually reclaimed space.
///
/// Deliberately narrow. Only folders Roblox rebuilds on its own are listed:
/// caches, logs, crash dumps. Nothing here touches settings, saved logins, or
/// anything the user would have to set up again — the worst case of emptying
/// all of it is a slower first launch while the cache refills.
/// </remarks>
public static class RobloxCache
{
    /// <summary>
    /// The folders worth clearing on this platform.
    /// </summary>
    /// <remarks>
    /// The Windows entries exist so the page can be exercised here. They are
    /// the real Windows paths, not stand-ins, so the sizes shown while
    /// developing are true ones.
    /// </remarks>
    public static IReadOnlyList<CacheFolder> Folders()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsMacOS())
        {
            string library = Path.Combine(home, "Library");

            return new[]
            {
                new CacheFolder("Asset cache", Path.Combine(library, "Caches", "com.roblox.RobloxPlayer")),
                new CacheFolder("Studio cache", Path.Combine(library, "Caches", "com.roblox.RobloxStudio")),
                new CacheFolder("Logs", Path.Combine(library, "Logs", "Roblox")),
                new CacheFolder("Crash reports", Path.Combine(library, "Logs", "DiagnosticReports"))
            };
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return new[]
        {
            new CacheFolder("Logs", Path.Combine(local, "Roblox", "logs")),
            new CacheFolder("Asset cache", Path.Combine(local, "Temp", "Roblox"))
        };
    }

    /// <summary>
    /// Adds up what is sitting in those folders.
    /// </summary>
    /// <remarks>
    /// Every failure is skipped rather than thrown. A cache directory is being
    /// written to by a running Roblox while this walks it, so files disappear
    /// mid-enumeration as a matter of course — that is normal, not an error.
    /// </remarks>
    public static CacheReport Measure(IEnumerable<CacheFolder> folders)
    {
        long bytes = 0;
        int files = 0;
        int counted = 0;

        foreach (CacheFolder folder in folders)
        {
            if (!folder.Exists) continue;

            counted++;

            foreach (string file in Walk(folder.Path))
            {
                try
                {
                    bytes += new FileInfo(file).Length;
                    files++;
                }
                catch
                {
                    // Gone between the listing and the measure.
                }
            }
        }

        return new CacheReport(bytes, files, counted);
    }

    /// <summary>
    /// Empties the folders, leaving the folders themselves in place.
    /// </summary>
    /// <remarks>
    /// Contents rather than the directory, because Roblox holds handles on some
    /// of these while it runs. Deleting the directory would fail outright where
    /// deleting most of its contents succeeds, and a partial clear is the
    /// correct outcome — the files still in use are the ones being written now.
    /// </remarks>
    public static CacheReport Clear(IEnumerable<CacheFolder> folders)
    {
        long freed = 0;
        int files = 0;
        int cleared = 0;

        foreach (CacheFolder folder in folders)
        {
            if (!folder.Exists) continue;

            cleared++;

            foreach (string file in Walk(folder.Path))
            {
                try
                {
                    long size = new FileInfo(file).Length;

                    File.Delete(file);

                    freed += size;
                    files++;
                }
                catch
                {
                    // Held open by a running Roblox. Left alone.
                }
            }

            foreach (string directory in SubfoldersDeepestFirst(folder.Path))
            {
                try { Directory.Delete(directory); } catch { /* not empty, or in use */ }
            }
        }

        return new CacheReport(freed, files, cleared);
    }

    private static IEnumerable<string> Walk(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Deepest first, so a parent is only tried once its children are gone.</summary>
    /// <remarks>
    /// Sorted by separator count, not path length. Length is the obvious proxy
    /// and the wrong one — one long-named shallow folder outranks a short-named
    /// deep one, the parent gets tried first, fails because it still has
    /// children, and is left behind.
    /// </remarks>
    internal static IEnumerable<string> SubfoldersDeepestFirst(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .OrderByDescending(Depth)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    internal static int Depth(string path) =>
        path.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
}
