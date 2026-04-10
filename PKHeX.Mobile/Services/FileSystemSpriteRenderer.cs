using PKHeX.Core;
using PKHeX.Drawing.Mobile.Sprites;
using SkiaSharp;

namespace PKHeX.Mobile.Services;

/// <summary>
/// Loads Pokémon sprites from MAUI raw assets bundled in the APK.
/// Sprites are accessed via FileSystem.OpenAppPackageFileAsync("sprites/{name}.png").
/// </summary>
public sealed class FileSystemSpriteRenderer : PKHeX.Drawing.Mobile.Sprites.ISpriteRenderer
{
    private readonly Dictionary<string, SKBitmap> _cache = new();
    private SKBitmap? _empty;

    public int Width => 68;
    public int Height => 56;

    /// <summary>
    /// Pre-loads sprites for all non-empty slots in a box so painting is cache-hit only.
    /// HOME sprites are downloaded in parallel first; bundled sprites fill any gaps.
    /// Call this before InvalidateSurface.
    /// </summary>
    public async Task PreloadBoxAsync(PKM[] box)
    {
        // Kick off all HOME sprite downloads in parallel (network or disk cache)
        var homeSlots = box
            .Where(pk => pk.Species != 0)
            .Select(pk => ((ushort)pk.Species, pk.Form, pk.IsShiny));
        await HomeSpriteCacheService.PreloadAsync(homeSlots).ConfigureAwait(false);

        // Fall back: load bundled sprites for anything HOME couldn't provide
        foreach (var pk in box)
        {
            if (pk.Species == 0)
                continue;
            // Skip bundled load if HOME already has this one
            if (HomeSpriteCacheService.GetCached((ushort)pk.Species, pk.Form, pk.IsShiny) is not null)
                continue;

            var key = BuildKey(pk);
            if (!_cache.ContainsKey(key))
            {
                var bmp = await TryLoadAsync(key);
                // Form sprite missing — alias to form 0 so slot isn't blank
                if (bmp is null && pk.Form != 0)
                {
                    var baseKey = BuildBaseKey(pk);
                    if (!_cache.ContainsKey(baseKey))
                        await TryLoadAsync(baseKey);
                    if (_cache.TryGetValue(baseKey, out var baseBmp))
                        _cache[key] = baseBmp;
                }
            }
        }
    }

    public SKBitmap GetSprite(PKM pk)
    {
        // Prefer high-quality HOME sprite for all forms
        var home = HomeSpriteCacheService.GetCached((ushort)pk.Species, pk.Form, pk.IsShiny);
        if (home is not null) return home;

        // Fall back to bundled 2D sprite
        var key = BuildKey(pk);
        if (_cache.TryGetValue(key, out var bmp)) return bmp;
        if (pk.Form != 0 && _cache.TryGetValue(BuildBaseKey(pk), out var baseBmp))
            return baseBmp;
        return GetEmptySprite();
    }

    public SKBitmap GetSprite(ushort species, byte form, byte gender, uint formArg, bool shiny, EntityContext context)
    {
        var key = "b" + SpriteName.GetResourceStringSprite(species, form, gender, formArg, context, shiny);
        return _cache.GetValueOrDefault(key) ?? GetEmptySprite();
    }

    public SKBitmap GetBallSprite(byte ball)
    {
        var key = SpriteName.GetResourceStringBall(ball);
        return _cache.GetValueOrDefault(key) ?? GetEmptySprite();
    }

    public SKBitmap GetEmptySprite()
    {
        if (_empty is not null)
            return _empty;
        var bmp = new SKBitmap(Width, Height);
        bmp.Erase(SKColors.Transparent);
        return _empty = bmp;
    }

    private static string BuildKey(PKM pk)
    {
        uint formArg = pk is IFormArgument fa ? fa.FormArgument : 0u;
        return "b" + SpriteName.GetResourceStringSprite(pk.Species, pk.Form, pk.Gender, formArg, pk.Context, pk.IsShiny);
    }

    // Fallback key: form 0, no formArg — used when a form-specific sprite file is absent
    private static string BuildBaseKey(PKM pk)
        => "b" + SpriteName.GetResourceStringSprite(pk.Species, 0, pk.Gender, 0u, pk.Context, pk.IsShiny);

    private async Task<SKBitmap?> TryLoadAsync(string name)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync($"sprites/{name}.png");
            var bmp = SKBitmap.Decode(stream);
            if (bmp is not null)
                _cache[name] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
