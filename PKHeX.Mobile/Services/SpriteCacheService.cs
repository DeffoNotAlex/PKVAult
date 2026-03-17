using System.Net;

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

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a base64 data URI for the sprite GIF, or <c>null</c> if the
    /// sprite is unavailable (missing from CDN and not cached).
    /// Caches the file on first successful download.
    /// </summary>
    public static async Task<string?> GetDataUriAsync(string slug, bool shiny)
    {
        var cacheKey = shiny ? $"ani-shiny/{slug}" : $"ani/{slug}";
        var cachePath = Path.Combine(CacheRoot, cacheKey + ".gif");

        // Cache hit
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 50)
            return ToDataUri(await File.ReadAllBytesAsync(cachePath));

        // Deduplicate concurrent requests for the same slug
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
        var folder = shiny ? "ani-shiny" : "ani";
        var path   = Path.Combine(CacheRoot, folder, $"{slug}.gif");
        return File.Exists(path) && new FileInfo(path).Length > 50;
    }

    // ── Internal ──────────────────────────────────────────────────────────────

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

    private static string ToDataUri(byte[] gif)
        => $"data:image/gif;base64,{Convert.ToBase64String(gif)}";
}
