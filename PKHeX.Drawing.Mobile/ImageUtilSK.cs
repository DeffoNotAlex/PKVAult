using System.Runtime.InteropServices;
using SkiaSharp;

namespace PKHeX.Drawing.Mobile;

/// <summary>
/// SkiaSharp port of PKHeX.Drawing.ImageUtil.
/// Uses <see cref="SKBitmap"/> (BGRA8888) instead of System.Drawing.Bitmap.
/// Pixel byte order is identical between the two, so all channel math is preserved.
/// </summary>
public static class ImageUtilSK
{
    // --- Bitmap construction ---

    public static SKBitmap GetBitmap(ReadOnlySpan<byte> data, int width, int height)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        bmp.Bytes = data.ToArray();
        return bmp;
    }

    public static SKBitmap CreateEmpty(int width, int height)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        bmp.Erase(SKColors.Transparent);
        return bmp;
    }

    // --- Compositing ---

    public static SKBitmap LayerImage(SKBitmap baseLayer, SKBitmap overLayer, int x, int y)
    {
        var bmp = baseLayer.Copy();
        using var canvas = new SKCanvas(bmp);
        canvas.DrawBitmap(overLayer, x, y);
        return bmp;
    }

    public static SKBitmap LayerImage(SKBitmap baseLayer, SKBitmap overLayer, int x, int y, double transparency)
    {
        var faded = CopyChangeOpacity(overLayer, transparency);
        return LayerImage(baseLayer, faded, x, y);
    }

    // --- Copy + transform ---

    public static SKBitmap CopyChangeOpacity(SKBitmap img, double trans)
    {
        var bmp = img.Copy();
        var bytes = bmp.Bytes;
        SetAllTransparencyTo(bytes, trans);
        bmp.Bytes = bytes;
        return bmp;
    }

    public static SKBitmap CopyChangeAllColorTo(SKBitmap img, SKColor c)
    {
        var bmp = img.Copy();
        var bytes = bmp.Bytes;
        ChangeAllColorTo(bytes, c);
        bmp.Bytes = bytes;
        return bmp;
    }

    public static SKBitmap CopyChangeTransparentTo(SKBitmap img, SKColor c, byte trans, int start = 0, int end = -1)
    {
        var bmp = img.Copy();
        var bytes = bmp.Bytes;
        var span = bytes.AsSpan();
        if (end == -1)
            end = span.Length;
        SetAllTransparencyTo(span[start..end], c, trans);
        bmp.Bytes = bytes;
        return bmp;
    }

    public static SKBitmap CopyWritePixels(SKBitmap img, SKColor c, int start, int end)
    {
        var bmp = img.Copy();
        var bytes = bmp.Bytes;
        ChangeAllTo(bytes.AsSpan(), c, start, end);
        bmp.Bytes = bytes;
        return bmp;
    }

    // --- In-place pixel operations (operate on raw BGRA Span<byte>) ---

    public static void SetAllUsedPixelsOpaque(Span<byte> data)
    {
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] != 0)
                data[i + 3] = 0xFF;
        }
    }

    public static void RemovePixels(Span<byte> pixels, ReadOnlySpan<byte> original)
    {
        var arr = MemoryMarshal.Cast<byte, int>(pixels);
        for (int i = original.Length - 4; i >= 0; i -= 4)
        {
            if (original[i + 3] != 0)
                arr[i >> 2] = 0;
        }
    }

    public static void GlowEdges(Span<byte> data, byte blue, byte green, byte red, int width, int reach = 3, double amount = 0.0777)
    {
        PollutePixels(data, width, reach, amount);
        CleanPollutedPixels(data, blue, green, red);
    }

    private static void SetAllTransparencyTo(Span<byte> data, double trans)
    {
        for (int i = data.Length - 4; i >= 0; i -= 4)
            data[i + 3] = (byte)(data[i + 3] * trans);
    }

    private static void SetAllTransparencyTo(Span<byte> data, SKColor c, byte trans)
    {
        var arr = MemoryMarshal.Cast<byte, int>(data);
        // Pack as ARGB int; on little-endian stored as BGRA bytes, matching SKColorType.Bgra8888
        var value = (trans << 24) | (c.Red << 16) | (c.Green << 8) | c.Blue;
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] == 0)
                arr[i >> 2] = value;
        }
    }

    private static void BlendAllTransparencyTo(Span<byte> data, SKColor c, byte trans)
    {
        var arr = MemoryMarshal.Cast<byte, int>(data);
        var value = (trans << 24) | (c.Red << 16) | (c.Green << 8) | c.Blue;
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            var alpha = data[i + 3];
            if (alpha == 0)
                arr[i >> 2] = value;
            else if (alpha != 0xFF)
                arr[i >> 2] = BlendColor(arr[i >> 2], value);
        }
    }

    public static void ChangeAllColorTo(Span<byte> data, SKColor c)
    {
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] == 0)
                continue;
            data[i + 0] = c.Blue;
            data[i + 1] = c.Green;
            data[i + 2] = c.Red;
        }
    }

    private static void ChangeAllTo(Span<byte> data, SKColor c, int start, int end)
    {
        var arr = MemoryMarshal.Cast<byte, int>(data[start..end]);
        var value = (c.Alpha << 24) | (c.Red << 16) | (c.Green << 8) | c.Blue;
        arr.Fill(value);
    }

    public static void SetAllColorToGrayscale(Span<byte> data, float intensity)
    {
        if (intensity <= 0f)
            return;

        float inverse = 1f - intensity;
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] == 0)
                continue;
            byte grey = (byte)((0.3 * data[i + 2]) + (0.59 * data[i + 1]) + (0.11 * data[i + 0]));
            if (intensity >= 0.999f)
            {
                data[i + 0] = grey;
                data[i + 1] = grey;
                data[i + 2] = grey;
            }
            else
            {
                data[i + 0] = (byte)((data[i + 0] * inverse) + (grey * intensity));
                data[i + 1] = (byte)((data[i + 1] * inverse) + (grey * intensity));
                data[i + 2] = (byte)((data[i + 2] * inverse) + (grey * intensity));
            }
        }
    }

    private static int BlendColor(int color1, int color2, double amount = 0.2)
    {
        var a1 = (color1 >> 24) & 0xFF; var r1 = (color1 >> 16) & 0xFF;
        var g1 = (color1 >> 8) & 0xFF;  var b1 = color1 & 0xFF;
        var a2 = (color2 >> 24) & 0xFF; var r2 = (color2 >> 16) & 0xFF;
        var g2 = (color2 >> 8) & 0xFF;  var b2 = color2 & 0xFF;
        byte a = (byte)((a1 * amount) + (a2 * (1 - amount)));
        byte r = (byte)((r1 * amount) + (r2 * (1 - amount)));
        byte g = (byte)((g1 * amount) + (g2 * (1 - amount)));
        byte b = (byte)((b1 * amount) + (b2 * (1 - amount)));
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private const int PollutePixelColorIndex = 0;

    private static void PollutePixels(Span<byte> data, int width, int reach, double amount)
    {
        int stride = width * 4;
        int height = data.Length / stride;
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] == 0)
                continue;
            int x = (i % stride) / 4;
            int y = i / stride;
            int left = Math.Max(0, x - reach);
            int right = Math.Min(width - 1, x + reach);
            int top = Math.Max(0, y - reach);
            int bottom = Math.Min(height - 1, y + reach);
            for (int ix = left; ix <= right; ix++)
            {
                for (int iy = top; iy <= bottom; iy++)
                {
                    var c = 4 * (ix + (iy * width));
                    ref var b = ref data[c + PollutePixelColorIndex];
                    b += (byte)(amount * (0xFF - b));
                }
            }
        }
    }

    private static void CleanPollutedPixels(Span<byte> data, byte blue, byte green, byte red)
    {
        for (int i = data.Length - 4; i >= 0; i -= 4)
        {
            if (data[i + 3] != 0)
                continue;
            var transparency = data[i + PollutePixelColorIndex];
            if (transparency == 0)
                continue;
            data[i + 0] = blue;
            data[i + 1] = green;
            data[i + 2] = red;
            data[i + 3] = transparency;
        }
    }
}
