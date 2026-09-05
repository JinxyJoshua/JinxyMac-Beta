using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace JinxyMac.Core.Tests;

/// <summary>
/// Guards the one architectural rule that used to be enforced by the test
/// project's file list rather than by any test: <c>Core/</c> depends on
/// nothing from Avalonia.
/// </summary>
/// <remarks>
/// Until the previous change, <c>Testing/JinxyMac.Tests.csproj</c> linked in
/// every <c>Core/*.cs</c> file by hand and carried no Avalonia package
/// reference at all, so a <c>Core</c> file that wrote <c>using Avalonia...</c>
/// simply failed to compile. That project now references Avalonia (to decode
/// real bitmaps end to end for <c>MainWindow.KitArt.cs</c>, which is not part
/// of Core), so the same file would compile cleanly today. This test is the
/// replacement gate: it reads every <c>.cs</c> file under <c>Core/</c> off
/// disk and fails if any of them mentions Avalonia outside of a comment.
///
/// Core is meant to be portable logic — platform and UI types belong in the
/// window layer (see the <c>Avalonia.Media.Imaging.Bitmap</c> use in
/// <c>MainWindow.KitArt.cs</c>, which lives outside Core for exactly this
/// reason). A doc comment is allowed to *talk about* Avalonia — several Core
/// files describe formats "Avalonia's Skia decoder" reads — so this test
/// strips comments before it looks, rather than treating any mention of the
/// word as a hit.
/// </remarks>
public class CorePurityTests
{
    /// <summary>
    /// Matches a <c>using Avalonia;</c> directive or any <c>Avalonia.</c>
    /// qualified reference (a using directive naming a sub-namespace, or a
    /// fully-qualified type use), but not the bare word "Avalonia" on its
    /// own. That narrower match is what lets this very file talk about the
    /// rule — in doc comments and in the failure message below — without
    /// tripping over itself: none of that prose is a <c>using</c> directive
    /// or followed by a dot.
    /// </summary>
    private static readonly Regex Reference =
        new(@"\busing\s+Avalonia\s*;|\bAvalonia\.\w", RegexOptions.Compiled);

    [Fact]
    public void CoreFilesDoNotReferenceAvalonia()
    {
        string root = FindRepoRoot();
        string coreDir = Path.Combine(root, "Core");
        Assert.True(
            Directory.Exists(coreDir),
            $"Expected a Core/ directory at '{coreDir}' (found the repository root by its " +
            $"JinxyMac.csproj), but it does not exist. Without it this test cannot verify " +
            "that Core has no Avalonia dependency, so it must fail rather than pass quietly.");

        string[] files = Directory.GetFiles(coreDir, "*.cs", SearchOption.AllDirectories);
        Assert.True(
            files.Length > 0,
            $"Found Core/ at '{coreDir}' but it contains no .cs files. A purity test that " +
            "silently finds nothing to check is worse than no test at all, so this counts " +
            "as a failure.");

        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(file);
            bool inBlockComment = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string code = StripComment(lines[i], ref inBlockComment);
                if (Reference.IsMatch(code))
                {
                    string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    Assert.Fail(
                        $"{relative}:{i + 1} references Avalonia outside of a comment: " +
                        $"\"{code.Trim()}\". Core/ is portable logic and must not depend on " +
                        "Avalonia; platform and UI types belong in the window layer (see " +
                        "MainWindow.KitArt.cs, which exists specifically to keep Avalonia's " +
                        "Bitmap type out of Core). Move this code out of Core/, or drop the " +
                        "reference.");
                }
            }
        }
    }

    /// <summary>
    /// Removes line and block comments from one line of source, carrying
    /// block-comment state across lines via <paramref name="inBlockComment"/>.
    /// </summary>
    /// <remarks>
    /// A "//" is only treated as the start of a line comment when it is not
    /// immediately preceded by ':' — this lets a <c>"https://..."</c> literal
    /// (there are several, in URLs the Core files build or validate) pass
    /// through untouched instead of being mistaken for a comment.
    /// </remarks>
    private static string StripComment(string line, ref bool inBlockComment)
    {
        var kept = new StringBuilder(line.Length);
        int i = 0;
        while (i < line.Length)
        {
            if (inBlockComment)
            {
                int end = line.IndexOf("*/", i, StringComparison.Ordinal);
                if (end < 0) return kept.ToString();
                i = end + 2;
                inBlockComment = false;
                continue;
            }

            if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
            {
                inBlockComment = true;
                i += 2;
                continue;
            }

            if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/'
                && (i == 0 || line[i - 1] != ':'))
            {
                break;
            }

            kept.Append(line[i]);
            i++;
        }
        return kept.ToString();
    }

    /// <summary>
    /// Walks up from the test assembly's own directory to find the checkout
    /// root, identified by <c>JinxyMac.csproj</c>, rather than assuming any
    /// fixed relative path from the test's output directory.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "JinxyMac.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "CorePurityTests could not locate the repository root (a directory containing " +
            $"JinxyMac.csproj) by walking up from the test assembly's directory " +
            $"'{AppContext.BaseDirectory}'. Without it this test cannot verify that Core/ " +
            "has no Avalonia dependency, so it must fail rather than pass quietly.");
    }
}
