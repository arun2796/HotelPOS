using System.Text.Json;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Application.Features.Templates;

// The stored form of a layout: the same JSON the API speaks (camelCase, enums as names), so a template row reads
// like the request that saved it.
public static class TemplateJson
{
    public static string Serialize(TemplateLayout layout) => JsonSerializer.Serialize(layout, PosJson.Options);

    // A stored layout that no longer parses (hand-edited row) falls back to the default instead of stopping printing.
    public static TemplateLayout Deserialize(string json, PrintDocumentType type)
    {
        try
        {
            return JsonSerializer.Deserialize<TemplateLayout>(json, PosJson.Options) ?? TemplateLayout.DefaultFor(type);
        }
        catch (JsonException)
        {
            return TemplateLayout.DefaultFor(type);
        }
    }
}
