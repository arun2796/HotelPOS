using HotelPOS.Application.Common.Interfaces;
using SkiaSharp;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace HotelPOS.Infrastructure.Documents;

// QR codes (UPI payment links) as black-on-white PNG, 8 pixels per module with a 2-module quiet zone.
public sealed class QrCodeRenderer : IQrCodeRenderer
{
    private const int PixelsPerModule = 8;

    public byte[] Png(string payload)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
            [EncodeHintType.CHARACTER_SET] = "UTF-8",
            [EncodeHintType.MARGIN] = 2,
        };
        var matrix = new QRCodeWriter().encode(payload, BarcodeFormat.QR_CODE, 0, 0, hints);

        var size = matrix.Width * PixelsPerModule;
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Fill };
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                if (matrix[x, y])
                {
                    canvas.DrawRect(x * PixelsPerModule, y * PixelsPerModule, PixelsPerModule, PixelsPerModule, paint);
                }
            }
        }

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
