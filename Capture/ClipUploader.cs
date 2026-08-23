using System.Net.Http;

namespace JinxyMac.Capture;

public readonly record struct UploadResult(bool Success, string Message, string? Url);

/// <summary>
/// Turns a local clip into a shareable link.
/// </summary>
/// <remarks>
/// Catbox was chosen because it is the only option that satisfies all three
/// requirements at once: works on any machine, needs no account or OAuth, and
/// hands back a usable URL. The alternatives each fail one — Medal's API only
/// triggers its own installed client, and YouTube locks uploads from unaudited
/// API projects to private, which has no shareable link.
///
/// The trade is that the link is public to anyone holding it, and there is no
/// reliable delete. Callers must treat uploading as an explicit user action,
/// never something that happens automatically after a recording.
///
/// Carried over from the Windows build unchanged apart from the namespace,
/// including the User-Agent, which is load-bearing — see below.
/// </remarks>
public static class ClipUploader
{
    private const string Endpoint = "https://catbox.moe/user/api.php";

    /// <summary>Catbox's documented ceiling. Checked locally to fail fast rather than after a long upload.</summary>
    public const long MaxBytes = 200L * 1024 * 1024;

    private static readonly HttpClient Client = CreateClient();

    /// <summary>
    /// The upload client, which must identify itself.
    /// </summary>
    /// <remarks>
    /// HttpClient sends no User-Agent unless one is set, and Catbox answers an
    /// unidentified POST with an empty 200 rather than an error. That is why
    /// every upload on the Windows build reported "The host rejected it:" with
    /// nothing after the colon — there was no message to show. Verified
    /// directly: the same file, same endpoint, same form, succeeds with a
    /// User-Agent and returns an empty body without one.
    /// </remarks>
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "JinxyClicker/1.0 (+https://github.com/JinxyJoshua/JinxyClicker)");

        return client;
    }

    public static async Task<UploadResult> UploadAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path))
            return new UploadResult(false, "That file no longer exists.", null);

        var file = new FileInfo(path);

        if (file.Length == 0)
            return new UploadResult(false, "That file is empty.", null);

        if (file.Length > MaxBytes)
        {
            return new UploadResult(false,
                $"That file is {file.Length / 1024.0 / 1024.0:0} MB — the limit is {MaxBytes / 1024 / 1024} MB.",
                null);
        }

        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent("fileupload"), "reqtype" }
            };

            await using FileStream stream = File.OpenRead(path);
            using var content = new StreamContent(stream);
            form.Add(content, "fileToUpload", file.Name);

            // No userhash: anonymous upload, so nothing is tied to an account.
            using HttpResponseMessage response =
                await Client.PostAsync(Endpoint, form, token).ConfigureAwait(false);

            string body = (await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)).Trim();

            if (!response.IsSuccessStatusCode)
                return new UploadResult(false, $"Upload failed ({(int)response.StatusCode}). {Truncate(body)}", null);

            // An empty 200 is its own failure mode, and reporting it as a
            // rejection printed a message with nothing after the colon — which
            // said nothing about what to do next.
            if (body.Length == 0)
            {
                return new UploadResult(false,
                    "The host accepted the request but returned nothing. It may be busy or blocking uploads right now — try again shortly.",
                    null);
            }

            // Success is the bare URL in the body; errors come back as prose
            // with a 200, so the shape of the response is what has to be checked.
            return Uri.TryCreate(body, UriKind.Absolute, out Uri? uri)
                   && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? new UploadResult(true, "Uploaded.", body)
                : new UploadResult(false, $"The host rejected it: {Truncate(body)}", null);
        }
        catch (OperationCanceledException)
        {
            return new UploadResult(false, "Upload cancelled.", null);
        }
        catch (Exception error)
        {
            return new UploadResult(false, $"Upload failed: {Describe(error)}", null);
        }
    }

    /// <summary>
    /// Flattens an exception chain into one readable line.
    /// </summary>
    /// <remarks>
    /// .NET reports a failed TLS handshake as "The SSL connection could not be
    /// established, see inner exception" — and the inner exception is the only
    /// place the actual reason lives. Reporting just the outer message told the
    /// user to look at something they cannot see.
    /// </remarks>
    internal static string Describe(Exception error)
    {
        var parts = new List<string>();

        for (Exception? current = error; current != null; current = current.InnerException)
        {
            string message = current.Message.Trim();

            // The pointer to the inner exception is noise once it is included.
            message = message.Replace(", see inner exception.", "", StringComparison.OrdinalIgnoreCase)
                             .Replace("See the inner exception for details.", "", StringComparison.OrdinalIgnoreCase)
                             .Trim();

            if (message.Length > 0 && !parts.Contains(message)) parts.Add(message);
        }

        return Truncate(string.Join(" — ", parts));
    }

    private static string Truncate(string text) =>
        text.Length <= 200 ? text : text[..200] + "…";
}
