namespace JinxyMac.Capture;

/// <summary>
/// Splits a pre-built ffmpeg flag string into the tokens
/// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> expects.
/// </summary>
/// <remarks>
/// <see cref="CaptureBackend"/> hands back its flags as ready-to-run
/// command-line text — the shape ffmpeg documentation is written in, and the
/// shape the existing tests already check against — rather than as a token
/// list. That text is safe to split on whitespace almost everywhere, except
/// the one place it deliberately quotes a value
/// (<c>-i "1:none"</c>), which this respects the way a shell would: a
/// double-quoted span is one token, with the quotes themselves stripped.
///
/// This is only ever handed strings this codebase built itself — the fixed
/// flags <see cref="CaptureBackend"/> emits, never anything that holds a
/// user-chosen path. That is what keeps it safe to use at all: splitting an
/// arbitrary string on whitespace could never correctly carry a value with a
/// space or a quote of its own, which is exactly why the actual dangerous
/// part of the command — the clip folder, entirely user-controlled — is never
/// run through this. It is added straight onto <c>ArgumentList</c> as a single
/// token instead, so it needs no escaping at all.
/// </remarks>
internal static class ArgumentTokens
{
    public static IEnumerable<string> Split(string commandLine)
    {
        var token = new System.Text.StringBuilder();
        bool inQuotes = false;
        bool has = false;

        foreach (char c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                has = true;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (has)
                {
                    yield return token.ToString();
                    token.Clear();
                    has = false;
                }

                continue;
            }

            token.Append(c);
            has = true;
        }

        if (has) yield return token.ToString();
    }
}
