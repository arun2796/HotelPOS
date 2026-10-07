using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotelPOS.Contracts.Common;

/// <summary>
/// JSON settings shared by the API (MVC + SignalR) and the desktop client, so both sides agree on
/// casing and enum representation.
/// </summary>
public static class PosJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options);
        return options;
    }

    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
        {
            options.Converters.Add(new JsonStringEnumConverter());
        }
    }
}
