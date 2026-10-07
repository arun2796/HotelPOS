namespace HotelPOS.Infrastructure.Persistence.Configurations;

internal static class ConfigurationHelpers
{
    public static string EnumCheck<TEnum>(string column)
        where TEnum : struct, Enum =>
        $"\"{column}\" IN ({string.Join(", ", Enum.GetValues<TEnum>().Select(v => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)))})";
}
