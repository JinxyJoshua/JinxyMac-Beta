using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JinxyMac.Core;

/// <summary>
/// What macOS thinks this app is, in a form someone can read off the screen and
/// send back.
/// </summary>
/// <remarks>
/// Written because the Mac problems cannot be reproduced here: the developer has
/// no Mac, so every round of "it says BLOCKED" has cost a day and produced one
/// sentence. These are the facts that would have answered it the first time —
/// the name the app is signed under, whether the bundle carries a seal, where it
/// is running from, and what macOS answers when asked about Accessibility.
///
/// <para>Everything here is read rather than assumed. The signing identifier
/// comes out of the running binary's own code signature, not from what the build
/// script intended to put there, because the whole point is to find out when
/// those two disagree.</para>
/// </remarks>
public static class MacDiagnostics
{
    /// <summary>
    /// The report, as plain lines. Facts macOS alone can answer are passed in by
    /// the window layer, which owns the permission checks.
    /// </summary>
    public static string Report(string appVersion, string accessibility, string screenRecording)
    {
        string exe = ExecutablePath();
        string? bundle = AppBundlePath(AppContext.BaseDirectory);

        var lines = new List<string>
        {
            $"Jinxy {appVersion} on {(OperatingSystem.IsMacOS() ? "macOS" : "non-macOS")} "
                + Environment.OSVersion.Version,
            $"Runtime: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}",
            $"Accessibility: {accessibility}",
            $"Screen recording: {screenRecording}",
            $"App bundle: {bundle ?? "not running from a .app"}",
            $"Running from Applications: {(bundle?.Contains("/Applications/", StringComparison.Ordinal) == true ? "yes" : "no")}",
            $"Signed as: {SigningIdentifier(exe) ?? "no signature found"}",
            $"Bundle seal: {(bundle != null && HasBundleSeal(bundle) ? "present" : "none")}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The binary this process is running, as macOS sees it.</summary>
    private static string ExecutablePath()
    {
        try { return Environment.ProcessPath ?? ""; }
        catch { return ""; }
    }

    /// <summary>The .app this code is running from, or null if it is not in one.</summary>
    /// <remarks>
    /// "Not in one" is itself worth reporting: an app run from a folder rather
    /// than a bundle has no identity for macOS to attach a permission to.
    /// </remarks>
    public static string? AppBundlePath(string startDirectory)
    {
        try
        {
            for (DirectoryInfo? at = new(startDirectory.TrimEnd(Path.DirectorySeparatorChar));
                 at != null;
                 at = at.Parent)
            {
                if (at.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return at.FullName;
            }
        }
        catch
        {
            // An unreadable path is not worth a crash in a diagnostic.
        }

        return null;
    }

    /// <summary>
    /// Whether the bundle carries a code seal, which is what
    /// <c>Contents/_CodeSignature/CodeResources</c> is.
    /// </summary>
    /// <remarks>
    /// Reported because it is the leading suspect for Accessibility not
    /// sticking: the binary is signed but the bundle around it is not, and macOS
    /// may well want the bundle signed before it will match a permission to the
    /// app. Sealing it needs a tool that can seal non-Mach-O files, which the
    /// one available on a PC cannot — see the 1.2.3 release, which shipped an
    /// incomplete seal and would not launch at all.
    /// </remarks>
    public static bool HasBundleSeal(string bundlePath) =>
        File.Exists(Path.Combine(bundlePath, "Contents", "_CodeSignature", "CodeResources"));

    /// <summary>
    /// The identifier a Mach-O is signed under, read out of its code signature.
    /// </summary>
    /// <remarks>
    /// macOS files an Accessibility grant under the app's bundle identifier and
    /// then checks the signing identifier of the process asking for it. The .NET
    /// SDK signs every app's launcher under the name "apphost", which matches
    /// nothing, so the build re-signs it as the bundle identifier. This reads
    /// back what actually ended up in the shipped binary, because a build step
    /// that silently did not run is exactly the kind of thing worth catching.
    ///
    /// Parsed by hand rather than by asking macOS: this has to give an answer on
    /// the PC where the code is written, too, and the format is a header, a
    /// table of blobs, and a string.
    /// </remarks>
    public static string? SigningIdentifier(string machOPath)
    {
        try
        {
            byte[] file = File.ReadAllBytes(machOPath);
            if (file.Length < 32) return null;

            // 64-bit Mach-O, little endian. Anything else is not a binary this
            // app ships.
            if (!(file[0] == 0xCF && file[1] == 0xFA && file[2] == 0xED && file[3] == 0xFE)) return null;

            int commands = BitConverter.ToInt32(file, 16);
            int at = 32;
            int signatureOffset = 0;

            for (int i = 0; i < commands && at + 8 <= file.Length; i++)
            {
                int command = BitConverter.ToInt32(file, at);
                int size = BitConverter.ToInt32(file, at + 4);
                if (size <= 0) return null;

                // LC_CODE_SIGNATURE
                if (command == 0x1D)
                {
                    signatureOffset = BitConverter.ToInt32(file, at + 8);
                    break;
                }

                at += size;
            }

            if (signatureOffset <= 0 || signatureOffset + 12 > file.Length) return null;

            // The embedded signature is a SuperBlob: a magic, a length, a count,
            // then that many (type, offset) pairs. Big endian throughout.
            if (BigEndian(file, signatureOffset) != 0xFADE0CC0) return null;

            int count = (int)BigEndian(file, signatureOffset + 8);

            for (int i = 0; i < count; i++)
            {
                int entry = signatureOffset + 12 + i * 8;
                if (entry + 8 > file.Length) return null;

                int blob = signatureOffset + (int)BigEndian(file, entry + 4);
                if (blob + 24 > file.Length) continue;

                // The CodeDirectory is the blob that carries the identifier.
                if (BigEndian(file, blob) != 0xFADE0C02) continue;

                int identifierOffset = blob + (int)BigEndian(file, blob + 20);
                if (identifierOffset <= blob || identifierOffset >= file.Length) return null;

                int end = Array.IndexOf(file, (byte)0, identifierOffset);
                if (end < 0) return null;

                return Encoding.UTF8.GetString(file, identifierOffset, end - identifierOffset);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static uint BigEndian(byte[] bytes, int at) =>
        (uint)((bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3]);
}
