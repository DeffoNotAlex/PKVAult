using System.Net;
using PKHeX.Core;

namespace PKHeX.Mobile.Services;

/// <summary>
/// Downloads animated sprites from the Pokémon Showdown CDN on first request
/// and caches them in AppDataDirectory for fully offline reuse.
///
/// Cache layout:
///   AppDataDirectory/sprites/ani/{slug}.gif
///   AppDataDirectory/sprites/ani-shiny/{slug}.gif
/// </summary>
public static class SpriteCacheService
{
    private static readonly string CacheRoot =
        Path.Combine(FileSystem.AppDataDirectory, "sprites");

    private const string BaseUrl = "https://play.pokemonshowdown.com/sprites";

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(12),
    };

    // In-flight guard so rapid cursor moves don't stack duplicate downloads
    private static readonly HashSet<string> _inFlight = [];
    private static readonly object          _lock     = new();

    // ── Bulk download state ───────────────────────────────────────────────────

    public static bool IsBulkDownloading { get; private set; }

    /// <summary>(Downloaded, Total, Failed) — updated during bulk download.</summary>
    public static (int Done, int Total, int Failed) BulkProgress { get; private set; }

    /// <summary>Fired on the main thread each time a sprite completes during bulk download.</summary>
    public static event Action<int, int>? BulkProgressChanged; // done, total

    /// <summary>Fired on the main thread for each completed slug. bool = success.</summary>
    public static event Action<string, bool>? SlugCompleted;

    private const int MaxSpecies  = 1025;
    private const int Concurrency = 5;

    /// <summary>
    /// Downloads all animated sprites that aren't already cached on disk,
    /// enumerating species 1–<see cref="MaxSpecies"/> plus all known form variants.
    /// Safe to fire-and-forget; progress is observable via <see cref="BulkProgressChanged"/>.
    /// </summary>
    public static async Task BulkDownloadAllAsync(CancellationToken ct = default)
    {
        if (IsBulkDownloading) return;

        var queue = BuildSlugQueue();

        int total  = queue.Count;
        int done   = 0;
        int failed = 0;

        IsBulkDownloading = true;
        BulkProgress      = (0, total, 0);
        MainThread.BeginInvokeOnMainThread(() => BulkProgressChanged?.Invoke(0, total));

        if (total > 0)
        {
            using var sem = new SemaphoreSlim(Concurrency);
            var tasks = queue.Select(async item =>
            {
                await sem.WaitAsync(ct).ConfigureAwait(false);
                bool ok = false;
                try
                {
                    if (ct.IsCancellationRequested) return;
                    var bytes = await DownloadAsync(item.Slug, item.Shiny).ConfigureAwait(false);
                    if (bytes is not null)
                    {
                        var path = CachePath(item.Slug, item.Shiny);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
                        ok = true;
                    }
                    else
                        Interlocked.Increment(ref failed);
                }
                catch { Interlocked.Increment(ref failed); }
                finally
                {
                    sem.Release();
                    int d = Interlocked.Increment(ref done);
                    BulkProgress = (d, total, failed);
                    string logSlug = item.Shiny ? $"{item.Slug} (shiny)" : item.Slug;
                    bool  logOk   = ok;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        BulkProgressChanged?.Invoke(d, total);
                        SlugCompleted?.Invoke(logSlug, logOk);
                    });
                }
            }).ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        IsBulkDownloading = false;
        BulkProgress      = (done, total, failed);
        MainThread.BeginInvokeOnMainThread(() => BulkProgressChanged?.Invoke(done, total));
    }

    /// <summary>How many regular animated sprites are already on disk.</summary>
    public static int CountCached()
    {
        int count = 0;
        var strings = GameInfo.GetStrings("en");
        for (ushort sp = 1; sp <= MaxSpecies; sp++)
        {
            var slug = ToShowdownSlug(strings.Species[sp]);
            if (IsCached(slug, false)) count++;
        }
        return count;
    }

    private static List<(string Slug, bool Shiny)> BuildSlugQueue()
    {
        var seen  = new HashSet<string>(StringComparer.Ordinal);
        var queue = new List<(string Slug, bool Shiny)>(MaxSpecies * 2 + 500);
        var strings = GameInfo.GetStrings("en");

        for (ushort sp = 1; sp <= MaxSpecies; sp++)
        {
            var info     = PersonalTable.SV.GetFormEntry(sp, 0);
            var baseSlug = ToShowdownSlug(strings.Species[sp]);

            EnqueueSlug(baseSlug, seen, queue);

            for (byte form = 1; form < info.FormCount; form++)
            {
                var formName   = ShowdownParsing.GetStringFromForm(form, strings, sp, EntityContext.Gen9);
                var formSuffix = ToShowdownFormSuffix(formName);
                if (formSuffix.Length == 0) continue;

                var slug = $"{baseSlug}-{formSuffix}";
                EnqueueSlug(slug, seen, queue);
            }
        }

        return queue;

        static void EnqueueSlug(string slug, HashSet<string> seen, List<(string, bool)> queue)
        {
            if (!seen.Add(slug)) return;
            if (!IsCached(slug, false)) queue.Add((slug, false));
            if (!IsCached(slug, true))  queue.Add((slug, true));
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a base64 data URI for the sprite GIF, or <c>null</c> if the
    /// sprite is unavailable (missing from CDN and not cached).
    /// Caches the file on first successful download.
    /// </summary>
    public static async Task<string?> GetDataUriAsync(string slug, bool shiny)
    {
        var cachePath = CachePath(slug, shiny);

        // Cache hit
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 50)
            return ToDataUri(await File.ReadAllBytesAsync(cachePath));

        // Deduplicate concurrent requests for the same slug
        var cacheKey = shiny ? $"ani-shiny/{slug}" : $"ani/{slug}";
        lock (_lock)
        {
            if (_inFlight.Contains(cacheKey)) return null;
            _inFlight.Add(cacheKey);
        }

        try
        {
            var bytes = await DownloadAsync(slug, shiny);
            if (bytes is null) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllBytesAsync(cachePath, bytes);
            return ToDataUri(bytes);
        }
        finally
        {
            lock (_lock) _inFlight.Remove(cacheKey);
        }
    }

    /// <summary>Whether a sprite is already on disk (no network needed).</summary>
    public static bool IsCached(string slug, bool shiny)
    {
        var path = CachePath(slug, shiny);
        return File.Exists(path) && new FileInfo(path).Length > 50;
    }

    // ── Slug helpers (also used by GamePage) ─────────────────────────────────

    /// <summary>
    /// Converts a species display name to a Showdown CDN slug.
    /// E.g. "Mr. Mime" → "mrmime", "Nidoran♀" → "nidoranf".
    /// </summary>
    public static string ToShowdownSlug(string speciesName)
    {
        var s = speciesName.ToLowerInvariant()
            .Replace("♀", "f").Replace("♂", "m")
            .Replace("é", "e");
        return System.Text.RegularExpressions.Regex.Replace(s, "[^a-z0-9]", "");
    }

    /// <summary>
    /// Converts a PKHeX form display name to a Showdown CDN form suffix.
    /// E.g. "Origin Forme" → "origin", "Sandy Cloak" → "sandy".
    /// Returns empty string if no usable suffix can be derived.
    /// </summary>
    public static string ToShowdownFormSuffix(string formName)
    {
        if (string.IsNullOrWhiteSpace(formName)) return "";

        var s = formName.ToLowerInvariant()
            .Replace("é", "e")
            .Replace("♀", "f")
            .Replace("♂", "m");

        string[] dropSuffixes = [" forme", " form", " mode", " cloak", " style",
                                  " size", " rider", " pattern", " face", " plumage"];
        foreach (var suffix in dropSuffixes)
        {
            if (s.EndsWith(suffix, StringComparison.Ordinal))
            {
                s = s[..^suffix.Length];
                break;
            }
        }

        s = s.Replace(' ', '-').Replace('_', '-');
        s = System.Text.RegularExpressions.Regex.Replace(s, "[^a-z0-9-]", "");
        return s.Trim('-');
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static async Task<byte[]?> DownloadAsync(string slug, bool shiny)
    {
        var folder     = shiny ? "ani-shiny"     : "ani";
        var gen5Folder = shiny ? "gen5ani-shiny" : "gen5ani";

        string[] urls =
        [
            $"{BaseUrl}/{folder}/{slug}.gif",
            $"{BaseUrl}/{gen5Folder}/{slug}.gif",
        ];

        foreach (var url in urls)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                if (bytes.Length > 50) return bytes;
            }
            catch { }
        }
        return null;
    }

    private static string CachePath(string slug, bool shiny)
    {
        var folder = shiny ? "ani-shiny" : "ani";
        return Path.Combine(CacheRoot, folder, $"{slug}.gif");
    }

    private static string ToDataUri(byte[] gif)
        => $"data:image/gif;base64,{Convert.ToBase64String(gif)}";
}
