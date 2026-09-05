using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace JinxyMac.Core;

/// <summary>
/// Downloads the kit pictures the app does not have yet.
/// </summary>
/// <remarks>
/// Runs in the background, and only for kits with no picture in either folder.
/// Since the install ships one for every kit on the roster, the usual outcome is
/// that it finds nothing to do and never touches the network at all.
///
/// Everything about it fails quiet. No network, a rate-limited reply, a kit the
/// wiki has never heard of: all end with that kit showing its name, which is
/// what the page already does for a kit nobody has given a picture.
/// </remarks>
public static class KitArtFetch
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>Downloads whatever is missing, reporting after each batch.</summary>
    /// <param name="kits">Every kit on the roster.</param>
    /// <param name="batchDone">
    /// Called on the calling thread after each batch, so the page can show the
    /// pictures arriving rather than all of them at the end.
    /// </param>
    public static async Task RunAsync(
        IEnumerable<string> kits, Func<Task>? batchDone, CancellationToken token)
    {
        List<string> missing = kits.Where(k => KitImages.Find(k) == null).ToList();

        if (missing.Count == 0) return;

        try
        {
            using var http = new HttpClient { Timeout = Timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("JinxyClicker (kit art)");

            foreach (List<string> batch in KitArt.Batches(missing))
            {
                if (token.IsCancellationRequested) return;

                if (await FetchBatchAsync(http, batch, token).ConfigureAwait(true) == 0)
                    continue;

                if (batchDone != null) await batchDone().ConfigureAwait(true);
            }
        }
        catch
        {
            // Pictures are a nicety. Nothing here is worth surfacing.
        }
    }

    /// <returns>How many pictures this batch actually saved.</returns>
    private static async Task<int> FetchBatchAsync(
        HttpClient http, List<string> batch, CancellationToken token)
    {
        int saved = 0;

        try
        {
            IEnumerable<string> titles = batch.SelectMany(
                k => KitArt.TitlesFor(k.Replace(' ', '_')));

            string json = await http.GetStringAsync(KitArt.QueryUrl(titles), token)
                .ConfigureAwait(false);

            Dictionary<string, string> urls = KitArt.ReadUrls(json);

            foreach (string kit in batch)
            {
                if (token.IsCancellationRequested) return saved;

                // Matched back by name rather than by position: the API returns
                // pages in its own order and omits the ones it does not have.
                //
                // The unqualified name is tried too, because a kit the game
                // calls "Zeno (Wizard)" is filed on the wiki as "Zeno" and
                // comes back under that title.
                if (!urls.TryGetValue(kit, out string? url)
                    && !urls.TryGetValue(KitArt.WithoutQualifier(kit).Replace('_', ' '), out url))
                {
                    continue;
                }
                if (!KitArt.IsWikiImage(url)) continue;

                if (await SaveAsync(http, kit, url, token).ConfigureAwait(false)) saved++;
            }
        }
        catch
        {
            // One bad batch does not stop the rest.
        }

        return saved;
    }

    private static async Task<bool> SaveAsync(
        HttpClient http, string kit, string url, CancellationToken token)
    {
        try
        {
            byte[] bytes = await http.GetByteArrayAsync(url, token).ConfigureAwait(false);

            // A truncated or error-page response is not a picture. Writing it
            // would leave a file that exists, never decodes, and stops the app
            // ever trying again.
            if (bytes.Length < 512) return false;

            // Named after what actually arrived rather than after what was
            // hoped for. Saving WebP as ".png" is what put a folder of
            // mislabelled files on every machine, and mislabelled is how they
            // came to be decoded down a path that threw the transparency away.
            string extension = KitArt.ExtensionFor(bytes);
            if (extension.Length == 0) return false;

            string target = KitImages.PathFor(kit, extension);
            if (target.Length == 0) return false;

            // No need to clear an earlier picture first: only kits that have
            // none in either folder reach here, and Find has already looked
            // under every extension. Deleting before writing would only risk
            // losing a file if the write then failed.

            await File.WriteAllBytesAsync(target, bytes, token).ConfigureAwait(false);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
