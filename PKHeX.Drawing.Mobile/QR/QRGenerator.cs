using QRCoder;

namespace PKHeX.Drawing.Mobile.QR;

/// <summary>
/// Generates QR code PNG images using QRCoder without any System.Drawing dependency.
/// </summary>
public static class QRGenerator
{
    /// <summary>
    /// Encodes <paramref name="message"/> as a QR code and returns raw PNG bytes.
    /// </summary>
    /// <param name="message">The string to encode (e.g. a PKHeX QR URL).</param>
    /// <param name="pixelsPerModule">Size of each QR module in pixels. Default 10 gives ~420px for typical codes.</param>
    public static byte[] GeneratePng(string message, int pixelsPerModule = 10)
    {
        using var data = QRCodeGenerator.GenerateQrCode(message, QRCodeGenerator.ECCLevel.Q);
        using var code = new PngByteQRCode(data);
        return code.GetGraphic(pixelsPerModule);
    }
}
