using System.Buffers.Binary;

namespace HotelPOS.Application.Common;

/// <summary>
/// Converts row versions (PostgreSQL xmin values) to and from the opaque base64 strings used in DTOs.
/// Clients only echo the string back; they never interpret it.
/// </summary>
public static class RowVersions
{
    public static string Encode(uint rowVersion)
    {
        if (rowVersion == 0)
        {
            return string.Empty;
        }

        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, rowVersion);
        return Convert.ToBase64String(bytes);
    }

    public static bool TryDecode(string? value, out uint rowVersion)
    {
        rowVersion = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[8];
        if (!Convert.TryFromBase64String(value, bytes, out var written) || written != sizeof(uint))
        {
            return false;
        }

        rowVersion = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        return rowVersion != 0;
    }

    public static bool Matches(uint current, string? provided) =>
        TryDecode(provided, out var decoded) && decoded == current;
}
