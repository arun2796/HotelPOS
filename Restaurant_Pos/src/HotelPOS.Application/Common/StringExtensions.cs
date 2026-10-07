namespace HotelPOS.Application.Common;

public static class StringExtensions
{
    public static string ToCamelCase(this string value) =>
        string.IsNullOrEmpty(value) || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];
}
