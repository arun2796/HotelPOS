using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HotelPOS.Desktop.Services.Printing;

// A picture (the logo) as an ESC/POS raster image (GS v 0): scaled to at most maxDots wide, one bit per dot, dark
// pixels printed. A picture that cannot be decoded prints nothing rather than garbage.
public static class EscPosRaster
{
    public static byte[] Encode(byte[] png, int maxDots)
    {
        try
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource source = decoder.Frames[0];
            var scale = Math.Min(1.0, (double)maxDots / source.PixelWidth);
            if (scale < 1.0)
            {
                source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            }

            var gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            var width = gray.PixelWidth;
            var height = gray.PixelHeight;
            var pixels = new byte[width * height];
            gray.CopyPixels(pixels, width, 0);

            var bytesPerRow = (width + 7) / 8;
            var output = new List<byte> { 0x1D, (byte)'v', (byte)'0', 0, (byte)(bytesPerRow % 256), (byte)(bytesPerRow / 256), (byte)(height % 256), (byte)(height / 256) };
            for (var y = 0; y < height; y++)
            {
                for (var b = 0; b < bytesPerRow; b++)
                {
                    byte packed = 0;
                    for (var bit = 0; bit < 8; bit++)
                    {
                        var x = (b * 8) + bit;
                        if (x < width && pixels[(y * width) + x] < 128)
                        {
                            packed |= (byte)(0x80 >> bit);
                        }
                    }

                    output.Add(packed);
                }
            }

            output.Add((byte)'\n');
            return output.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException or InvalidOperationException)
        {
            return Array.Empty<byte>();
        }
    }
}
