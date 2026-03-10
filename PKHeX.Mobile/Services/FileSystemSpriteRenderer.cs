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
    /// Call this before InvalidateSurface.
    /// </summary>
    public async Task PreloadBoxAsync(PKM[] box)
    {
        foreach (var pk in box)
        {
            if (pk.Species == 0)
                continue;
            var key = BuildKey(pk);
            if (!_cache.ContainsKey(key))
                await TryLoadAsync(key);
        }
    }

    public SKBitmap GetSprite(PKM pk) =>
        _cache.GetValueOrDefault(BuildKey(pk)) ?? GetEmptySprite();

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
