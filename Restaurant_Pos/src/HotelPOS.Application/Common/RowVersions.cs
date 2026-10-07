namespace HotelPOS.Application.Common;

/// <summary>Converts SQL Server rowversion values to and from the base64 strings used in DTOs.</summary>
public static class RowVersions
{
    public static string Encode(byte[]? rowVersion) =>
        rowVersion is null || rowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(rowVersion);

    public static bool TryDecode(string? value, out byte[] rowVersion)
    {
        rowVersion = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(value);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool Matches(byte[] current, string? provided) =>
        TryDecode(provided, out var decoded) && current.AsSpan().SequenceEqual(decoded);
}
