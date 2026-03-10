using PKHeX.Core;
using SkiaSharp;

namespace PKHeX.Drawing.Mobile.Sprites;

/// <summary>
/// Returns colored placeholder boxes until real sprite resources are wired up in Phase 3.
/// Color is derived from the Pokémon's base stat total so boxes are visually distinct.
/// </summary>
public sealed class PlaceholderSpriteRenderer : ISpriteRenderer
{
    public int Width { get; } = 56;
    public int Height { get; } = 56;

    public SKBitmap GetSprite(PKM pk)
    {
        var bst = pk.PersonalInfo.BST;
        var color = ColorUtilSK.ColorBaseStatTotal(bst);
        return DrawPlaceholder(color, pk.IsShiny);
    }

    public SKBitmap GetSprite(ushort species, byte form, byte gender, uint formArg, bool shiny, EntityContext context)
    {
        return DrawPlaceholder(SKColors.LightSteelBlue, shiny);
    }

    public SKBitmap GetBallSprite(byte ball)
    {
        return DrawPlaceholder(SKColors.DarkRed, false, label: "●");
    }

    public SKBitmap GetEmptySprite()
    {
        var bmp = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        bmp.Erase(SKColors.Transparent);
        return bmp;
    }

    private SKBitmap DrawPlaceholder(SKColor fill, bool shiny, string? label = null)
    {
        var bmp = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        // Background box
        using var fillPaint = new SKPaint { Color = fill, IsAntialias = true };
        canvas.DrawRoundRect(2, 2, Width - 4, Height - 4, 6, 6, fillPaint);

        // Shiny indicator — gold border
        if (shiny)
        {
            using var borderPaint = new SKPaint
            {
                Color = SKColors.Gold,
                IsAntialias = true,
                IsStroke = true,
                StrokeWidth = 2,
            };
            canvas.DrawRoundRect(2, 2, Width - 4, Height - 4, 6, 6, borderPaint);
        }

        // Optional label
        if (label is not null)
        {
            using var textPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 24,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center,
            };
            canvas.DrawText(label, Width / 2f, (Height / 2f) + 10, textPaint);
        }

        return bmp;
    }
}
