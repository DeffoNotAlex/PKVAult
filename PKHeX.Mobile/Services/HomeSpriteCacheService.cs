using SkiaSharp;

namespace PKHeX.Mobile.Services;

/// <summary>
/// Downloads Pokémon HOME-style sprites from the PokeAPI sprites repository
/// on first request, then caches them on disk for fully offline reuse.
///
/// Cache layout:
///   CacheDirectory/home_sprites/{slug}.png
///   CacheDirectory/home_sprites/shiny/{slug}.png
/// where {slug} is the PokeAPI path segment, e.g. "666", "666-archipelago".
///
/// URL pattern (public GitHub raw, no API key needed):
///   https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/{slug}.png
///   https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/shiny/{slug}.png
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

    // ── Form slug table ───────────────────────────────────────────────────────
    // Maps (species, form) → PokeAPI HOME path segment for alternate forms.
    // Only includes forms that have distinct sprites in the PokeAPI HOME set.
    // All unlisted forms fall back to the base species number (form 0 sprite).
    // NOTE: Furfrou trims are intentionally omitted — PokeAPI HOME only has the
    //       Natural form; trims have no HOME sprite variant.
    private static readonly Dictionary<(ushort Species, byte Form), string> FormSlugs = new()
    {
        // Flabébé (669) — flower colours
        { (669, 1), "669-yellow" }, { (669, 2), "669-orange" },
        { (669, 3), "669-blue"   }, { (669, 4), "669-white"  },

        // Floette (670) — flower colours + Eternal Flower
        { (670, 1), "670-yellow" }, { (670, 2), "670-orange" },
        { (670, 3), "670-blue"   }, { (670, 4), "670-white"  },
        { (670, 5), "670-eternal" },

        // Florges (671) — flower colours
        { (671, 1), "671-yellow" }, { (671, 2), "671-orange" },
        { (671, 3), "671-blue"   }, { (671, 4), "671-white"  },

        // Shellos (422) / Gastrodon (423) — East Sea
        { (422, 1), "422-east" }, { (423, 1), "423-east" },

        // Basculin (550) — Blue-Striped
        { (550, 1), "550-blue-striped" },

        // Deerling (585) / Sawsbuck (586) — seasons
        { (585, 1), "585-summer" }, { (585, 2), "585-autumn" }, { (585, 3), "585-winter" },
        { (586, 1), "586-summer" }, { (586, 2), "586-autumn" }, { (586, 3), "586-winter" },

        // Pumpkaboo (710) / Gourgeist (711) — sizes
        { (710, 1), "710-small" }, { (710, 2), "710-large" }, { (710, 3), "710-super" },
        { (711, 1), "711-small" }, { (711, 2), "711-large" }, { (711, 3), "711-super" },

        // Vivillon (666) — 20 wing patterns
        { (666,  1), "666-polar"       }, { (666,  2), "666-tundra"      },
        { (666,  3), "666-continental" }, { (666,  4), "666-garden"      },
        { (666,  5), "666-elegant"     }, { (666,  6), "666-meadow"      },
        { (666,  7), "666-modern"      }, { (666,  8), "666-marine"      },
        { (666,  9), "666-archipelago" }, { (666, 10), "666-high-plains" },
        { (666, 11), "666-sandstorm"   }, { (666, 12), "666-river"       },
        { (666, 13), "666-monsoon"     }, { (666, 14), "666-savanna"     },
        { (666, 15), "666-sun"         }, { (666, 16), "666-ocean"       },
        { (666, 17), "666-jungle"      }, { (666, 18), "666-fancy"       },
        { (666, 19), "666-pokeball"    },

        // Rotom (479) appliance forms
        { (479, 1), "479-heat"  }, { (479, 2), "479-wash"  },
        { (479, 3), "479-frost" }, { (479, 4), "479-fan"   },
        { (479, 5), "479-mow"   },

        // Oricorio (741) dance styles
        { (741, 1), "741-pom-pom" }, { (741, 2), "741-pau" }, { (741, 3), "741-sensu" },

        // Lycanroc (745)
        { (745, 1), "745-midnight" }, { (745, 2), "745-dusk" },

        // Meowstic (678) — female
        { (678, 1), "678-f" },

        // Indeedee (876) — female
        { (876, 1), "876-f" },

        // Basculegion (902) — female
        { (902, 1), "902-f" },

        // Oinkologne (916) — female
        { (916, 1), "916-f" },

        // Urshifu (892) — Rapid Strike style
        { (892, 1), "892-rapid-strike" },

        // Calyrex riders (898)
        { (898, 1), "898-ice" }, { (898, 2), "898-shadow" },

        // Zacian (888) / Zamazenta (889) — Crowned (corrected slugs)
        { (888, 1), "888-crowned-sword" }, { (889, 1), "889-crowned-shield" },

        // Eiscue (875) — No-Ice face
        { (875, 1), "875-noice" },

        // Morpeko (877) — Hangry mode
        { (877, 1), "877-hangry" },

        // Necrozma (800) — fused / Ultra forms (corrected slugs)
        { (800, 1), "800-dusk-mane" }, { (800, 2), "800-dawn-wings" }, { (800, 3), "800-ultra" },
    };

    /// <summary>
    /// Returns the PokeAPI HOME path segment (slug) for a given species+form.
    /// Returns <c>null</c> for non-zero forms that are not in the slug table,
    /// meaning no form-specific HOME sprite is available.
    /// </summary>
    public static string? GetHomeSlug(ushort species, byte form)
    {
        if (form == 0) return $"{species}";
        return FormSlugs.TryGetValue((species, form), out var slug) ? slug : null;
    }

    /// <summary>
    /// Returns the full PokeAPI HOME sprite URL for a species/form/shiny combination.
    /// Returns <c>null</c> if no HOME sprite is available for this form.
    /// </summary>
    public static string? GetHomeUrl(ushort species, byte form, bool shiny)
    {
        var slug = GetHomeSlug(species, form);
        if (slug is null) return null;
        return shiny ? $"{BaseUrl}/shiny/{slug}.png" : $"{BaseUrl}/{slug}.png";
    }

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
    /// Downloads all regular + shiny HOME sprites that aren't already on disk,
    /// including known form variants, using a sliding window of 5 concurrent requests.
    /// Safe to fire-and-forget; progress is observable via <see cref="BulkProgressChanged"/>.
    /// </summary>
    public static async Task BulkDownloadAsync(CancellationToken ct = default)
    {
        if (IsBulkDownloading) return;

        // Build queue: base forms for all species + all known form variants
        var queue = new List<(string Slug, bool Shiny)>(MaxSpecies * 2 + FormSlugs.Count * 2);
        for (ushort sp = 1; sp <= MaxSpecies; sp++)
        {
            var slug = $"{sp}";
            if (!File.Exists(DiskPath(slug, false))) queue.Add((slug, false));
            if (!File.Exists(DiskPath(slug, true)))  queue.Add((slug, true));
        }
        foreach (var (_, slug) in FormSlugs)
        {
            if (!File.Exists(DiskPath(slug, false))) queue.Add((slug, false));
            if (!File.Exists(DiskPath(slug, true)))  queue.Add((slug, true));
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
                bool ok = false;
                try
                {
                    if (ct.IsCancellationRequested) return;
                    var bytes = await DownloadAsync(item.Slug, item.Shiny).ConfigureAwait(false);
                    if (bytes is not null)
                    {
                        var disk = DiskPath(item.Slug, item.Shiny);
                        Directory.CreateDirectory(Path.GetDirectoryName(disk)!);
                        await File.WriteAllBytesAsync(disk, bytes, ct).ConfigureAwait(false);
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

    /// <summary>How many regular sprites are already on disk.</summary>
    public static int CountCached()
    {
        int count = 0;
        for (ushort sp = 1; sp <= MaxSpecies; sp++)
            if (File.Exists(DiskPath($"{sp}", false))) count++;
        return count;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a cached <see cref="SKBitmap"/> if the sprite has already been
    /// loaded this session, otherwise <c>null</c> (non-blocking).
    /// </summary>
    public static SKBitmap? GetCached(ushort species, byte form, bool shiny)
    {
        var slug = GetHomeSlug(species, form);
        if (slug is null) return null;
        lock (_lock)
            return _mem.GetValueOrDefault(MemKey(slug, shiny));
    }

    /// <summary>
    /// Returns the HOME sprite for <paramref name="species"/>/<paramref name="form"/>,
    /// downloading it on first access and caching to disk.
    /// Returns <c>null</c> if the download fails or no HOME sprite exists for this form.
    /// </summary>
    public static async Task<SKBitmap?> GetOrDownloadAsync(ushort species, byte form, bool shiny)
    {
        if (species == 0) return null;

        var slug = GetHomeSlug(species, form);
        if (slug is null) return null;   // unknown form variant — no HOME sprite

        var mkey = MemKey(slug, shiny);

        // 1. Memory cache
        lock (_lock)
        {
            if (_mem.TryGetValue(mkey, out var hit)) return hit;
        }

        // 2. Disk cache
        var disk = DiskPath(slug, shiny);
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
            var bytes = await DownloadAsync(slug, shiny).ConfigureAwait(false);
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
    public static Task PreloadAsync(IEnumerable<(ushort species, byte form, bool shiny)> slots)
    {
        var tasks = slots
            .Where(s => s.species != 0)
            .Distinct()
            .Select(s => GetOrDownloadAsync(s.species, s.form, s.shiny));
        return Task.WhenAll(tasks);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static async Task<byte[]?> DownloadAsync(string slug, bool shiny)
    {
        var url = shiny
            ? $"{BaseUrl}/shiny/{slug}.png"
            : $"{BaseUrl}/{slug}.png";
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

    private static string MemKey(string slug, bool shiny)
        => shiny ? $"s:{slug}" : $"n:{slug}";

    private static string DiskPath(string slug, bool shiny)
        => shiny
            ? Path.Combine(CacheRoot, "shiny", $"{slug}.png")
            : Path.Combine(CacheRoot, $"{slug}.png");
}
