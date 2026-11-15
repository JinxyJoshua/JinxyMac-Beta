using System;
using System.IO;

namespace JinxyMac.Core;

/// <summary>Why Roblox's settings file can or cannot be written.</summary>
public enum FlagAccess
{
    /// <summary>No Roblox.app was found to write into.</summary>
    NoRoblox,

    /// <summary>The settings file can be written.</summary>
    Ready,

    /// <summary>
    /// macOS refused the write.
    /// </summary>
    /// <remarks>
    /// The usual one, and the reason this whole type exists. Since Ventura,
    /// changing anything inside another application's bundle needs the App
    /// Management permission, and Roblox's settings file lives inside
    /// Roblox.app — there is nowhere else on a Mac that the client reads it
    /// from. Without that permission every write fails, and the old code
    /// reported it as "close Roblox and try again", which was useless advice.
    /// </remarks>
    Blocked,

    /// <summary>The file is held open by something, usually a running Roblox.</summary>
    Locked,

    /// <summary>Something else went wrong, carried verbatim rather than guessed at.</summary>
    Unknown
}

/// <summary>
/// Whether Roblox's settings can be written, and what to say if not.
/// </summary>
/// <remarks>
/// Pure, and separate from the writing, so the page can ask before offering a
/// button rather than after someone presses it and is told something wrong.
///
/// <para>Every message here names what the user has to do. The failure this
/// replaces said "close Roblox and try again" for every cause, including the
/// common one where closing Roblox changes nothing at all.</para>
/// </remarks>
public sealed record FlagAccessReport(FlagAccess State, string? Path = null, string? Detail = null)
{
    public bool CanWrite => State == FlagAccess.Ready;

    /// <summary>The sentence the page shows.</summary>
    public string Explain => State switch
    {
        FlagAccess.Ready => "Jinxy can write Roblox's settings.",

        FlagAccess.NoRoblox =>
            "No Roblox install found. Install Roblox, then come back.",

        FlagAccess.Blocked =>
            "macOS is stopping Jinxy from changing Roblox. Since macOS 13, an app "
            + "needs permission before it can alter another app, and Roblox's settings "
            + "live inside Roblox itself — there is nowhere else on a Mac the game "
            + "reads them from.",

        FlagAccess.Locked =>
            "Roblox is using its settings file right now. Quit Roblox and try again.",

        _ => "Roblox's settings could not be written."
    };

    /// <summary>What to do about it, when there is something to do.</summary>
    public string? Fix => State switch
    {
        FlagAccess.Blocked =>
            "Open Privacy & Security, switch Jinxy on under App Management, then reopen "
            + "Jinxy. If Jinxy is not listed — which happens to apps that are not signed "
            + "with a paid Apple developer account, and this one is not — run the command "
            + "below in Terminal instead. It creates the folder and hands it to you, which "
            + "Jinxy can then write without any permission at all.",

        _ => null
    };

    /// <summary>
    /// The Terminal fallback, for when macOS will not offer Jinxy the permission.
    /// </summary>
    /// <remarks>
    /// Creates the folder as root and then gives it to the user, so every write
    /// after this one is an ordinary write to a folder they own. Offered as text
    /// to copy and never run by the app: it needs an administrator password, and
    /// that is the user's to type, not something to be asked for on their behalf.
    /// </remarks>
    public string? TerminalFix => State == FlagAccess.Blocked && Path != null
        ? $"sudo mkdir -p \"{Path}\" && sudo chown -R \"$USER\" \"{Path}\""
        : null;

    /// <summary>One line for the diagnostics report.</summary>
    public string Line => Detail == null
        ? $"Roblox settings: {State}"
        : $"Roblox settings: {State} ({Detail})";

    /// <summary>Reads an exception as the reason behind it.</summary>
    /// <remarks>
    /// macOS answers a blocked bundle write with EPERM, which .NET surfaces as
    /// <see cref="UnauthorizedAccessException"/>. That is the same answer it
    /// gives for a file owned by somebody else, so this cannot prove App
    /// Management is the cause — but it is overwhelmingly the likely one, and
    /// the Terminal fallback fixes the ownership case too.
    /// </remarks>
    public static FlagAccessReport From(Exception error, string? path) => error switch
    {
        UnauthorizedAccessException => new FlagAccessReport(FlagAccess.Blocked, path, error.Message),
        DirectoryNotFoundException => new FlagAccessReport(FlagAccess.NoRoblox, path, error.Message),
        IOException => new FlagAccessReport(FlagAccess.Locked, path, error.Message),
        _ => new FlagAccessReport(FlagAccess.Unknown, path, error.Message)
    };
}
