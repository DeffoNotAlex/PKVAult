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

    // ── Bulk download state ───────────────────────────────────────────────────

    public static bool IsBulkDownloading { get; private set; }

    /// <summary>(Downloaded, Total, Failed) — updated during bulk download.</summary>
    public static (int Done, int Total, int Failed) BulkProgress { get; private set; }

    /// <summary>Fired on the main thread each time a sprite completes during bulk download.</summary>
    public static event Action<int, int>? BulkProgressChanged; // done, total

    private const int MaxSpecies  = 1025;
    private const int Concurrency = 5;

    /// <summary>
    /// Downloads all regular + shiny HOME sprites that aren't already on disk,
    /// using a sliding window of 5 concurrent requests.
    /// Safe to fire-and-forget; progress is observable via <see cref="BulkProgressChanged"/>.
    /// </summary>
    public static async Task BulkDownloadAsync(CancellationToken ct = default)
    {
        if (IsBulkDownloading) return;

        // Build queue — skip anything already cached on disk
        var queue = new List<(ushort Species, bool Shiny)>(MaxSpecies * 2);
        for (ushort sp = 1; sp <= MaxSpecies; sp++)
        {
            if (!File.Exists(DiskPath(sp, false))) queue.Add((sp, false));
            if (!File.Exists(DiskPath(sp, true)))  queue.Add((sp, true));
        }

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
                try
                {
                    if (ct.IsCancellationRequested) return;
                    var bytes = await DownloadAsync(item.Species, item.Shiny).ConfigureAwait(false);
                    if (bytes is not null)
                    {
                        var disk = DiskPath(item.Species, item.Shiny);
                        Directory.CreateDirectory(Path.GetDirectoryName(disk)!);
                        await File.WriteAllBytesAsync(disk, bytes, ct).ConfigureAwait(false);
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
                    MainThread.BeginInvokeOnMainThread(() => BulkProgressChanged?.Invoke(d, total));
                }
            }).ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        IsBulkDownloading = false;
        BulkProgress      = (done, total, failed);
        MainThread.BeginInvokeOnMainThread(() => BulkProgressChanged?.Invoke(done, total));
    }

    /// <summary>How many regular sprites are already on disk.</summary>
    public static int CountCached()
    {
        int count = 0;
        for (ushort sp = 1; sp <= MaxSpecies; sp++)
            if (File.Exists(DiskPath(sp, false))) count++;
        return count;
    }

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
