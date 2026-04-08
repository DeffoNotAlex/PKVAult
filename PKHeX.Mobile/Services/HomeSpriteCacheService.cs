using SkiaSharp;

namespace PKHeX.Mobile.Services;

/// <summary>
/// Downloads Pokémon HOME-style sprites from the PokeAPI sprites repository
/// on first request, then caches them on disk for fully offline reuse.
///
/// Cache layout:
///   CacheDirectory/home_sprites/{id}.png
///   CacheDirectory/home_sprites/shiny/{id}.png
///
/// URL pattern (public GitHub raw, no API key needed):
///   https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/{id}.png
///   https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/shiny/{id}.png
/// </summary>
public static class HomeSpriteCacheService
{
    private static readonly string CacheRoot =
        Path.Combine(FileSystem.CacheDirectory, "home_sprites");

    private const string BaseUrl =
        "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    // Decoded bitmaps kept in memory so GetCached() is zero-allocation on hot path
    private static readonly Dictionary<string, SKBitmap> _mem = new();
    private static readonly HashSet<string>              _inFlight = [];
    private static readonly object                       _lock = new();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a cached <see cref="SKBitmap"/> if the sprite has already been
    /// loaded this session, otherwise <c>null</c> (non-blocking).
    /// </summary>
    public static SKBitmap? GetCached(ushort species, bool shiny)
    {
        lock (_lock)
            return _mem.GetValueOrDefault(MemKey(species, shiny));
    }

    /// <summary>
    /// Returns the HOME sprite for <paramref name="species"/>, downloading it
    /// on first access and caching to disk. Returns <c>null</c> if the download
    /// fails (no network, species not in PokeAPI, etc.).
    /// </summary>
    public static async Task<SKBitmap?> GetOrDownloadAsync(ushort species, bool shiny)
    {
        if (species == 0) return null;

        var mkey = MemKey(species, shiny);

        // 1. Memory cache
        lock (_lock)
        {
            if (_mem.TryGetValue(mkey, out var hit)) return hit;
        }

        // 2. Disk cache
        var disk = DiskPath(species, shiny);
        if (File.Exists(disk) && new FileInfo(disk).Length > 100)
        {
            var bmp = SKBitmap.Decode(disk);
            if (bmp is not null)
            {
                lock (_lock) _mem[mkey] = bmp;
                return bmp;
            }
        }

        // 3. Network download (deduplicated)
        lock (_lock)
        {
            if (_inFlight.Contains(mkey)) return null;
            _inFlight.Add(mkey);
        }

        try
        {
            var bytes = await DownloadAsync(species, shiny).ConfigureAwait(false);
            if (bytes is null) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(disk)!);
            await File.WriteAllBytesAsync(disk, bytes).ConfigureAwait(false);

            using var ms = new MemoryStream(bytes);
            var decoded = SKBitmap.Decode(ms);
            if (decoded is not null)
                lock (_lock) _mem[mkey] = decoded;
            return decoded;
        }
        finally
        {
            lock (_lock) _inFlight.Remove(mkey);
        }
    }

    /// <summary>
    /// Preloads HOME sprites for a box in parallel. Safe to fire-and-forget or await.
    /// </summary>
    public static Task PreloadAsync(IEnumerable<(ushort species, bool shiny)> slots)
    {
        var tasks = slots
            .Where(s => s.species != 0)
            .Distinct()
            .Select(s => GetOrDownloadAsync(s.species, s.shiny));
        return Task.WhenAll(tasks);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static async Task<byte[]?> DownloadAsync(ushort species, bool shiny)
    {
        var url = shiny
            ? $"{BaseUrl}/shiny/{species}.png"
            : $"{BaseUrl}/{species}.png";
        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            return bytes.Length > 100 ? bytes : null;
        }
        catch
        {
            return null;
        }
    }

    private static string MemKey(ushort species, bool shiny)
        => shiny ? $"s{species}" : $"n{species}";

    private static string DiskPath(ushort species, bool shiny)
        => shiny
            ? Path.Combine(CacheRoot, "shiny", $"{species}.png")
            : Path.Combine(CacheRoot, $"{species}.png");
}
