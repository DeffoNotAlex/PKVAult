using PKHeX.Core;
using SkiaSharp;

namespace PKHeX.Drawing.Mobile.Sprites;

/// <summary>
/// Provides sprite images for Pokémon entities, balls, and types.
/// Phase 2: placeholder implementation. Phase 3: backed by real sprite resources.
/// </summary>
public interface ISpriteRenderer
{
    int Width { get; }
    int Height { get; }

    SKBitmap GetSprite(PKM pk);
    SKBitmap GetSprite(ushort species, byte form, byte gender, uint formArg, bool shiny, EntityContext context);
    SKBitmap GetBallSprite(byte ball);
    SKBitmap GetEmptySprite();
}
