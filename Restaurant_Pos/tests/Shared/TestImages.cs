using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace HotelPOS.Testing;

/// <summary>Builds real image files in memory for upload tests, without an imaging library.</summary>
internal static class TestImages
{
    /// <summary>A solid-colour RGB PNG of the given size.</summary>
    public static byte[] Png(int width, int height)
    {
        var row = new byte[1 + (width * 3)];
        for (var x = 0; x < width; x++)
        {
            row[1 + (x * 3)] = 200;
            row[2 + (x * 3)] = 60;
            row[3 + (x * 3)] = 40;
        }

        using var pixels = new MemoryStream();
        using (var zlib = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                zlib.Write(row);
            }
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: RGB

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", pixels.ToArray());
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    /// <summary>Bytes that are not an image, of the given size.</summary>
    public static byte[] Garbage(int length) => Enumerable.Repeat((byte)'x', length).ToArray();

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        stream.Write(buffer);
        stream.Write(typeBytes);
        stream.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc32(typeBytes.Concat(data)));
        stream.Write(buffer);
    }

    private static uint Crc32(IEnumerable<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
