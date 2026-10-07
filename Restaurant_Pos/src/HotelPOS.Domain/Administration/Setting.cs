using System.Globalization;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Administration;

public sealed class Setting : IAuditable
{
    private Setting()
    {
    }

    public Setting(string key, string value, SettingDataType dataType, string? description, bool isPublic)
    {
        Key = key;
        DataType = dataType;
        Description = description;
        IsPublic = isPublic;
        SetValue(value);
    }

    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public SettingDataType DataType { get; private set; }
    public string? Description { get; private set; }

    public bool IsPublic { get; private set; }

    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }

    public void SetValue(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!IsValidValue(DataType, trimmed))
        {
            throw new DomainException($"Value '{trimmed}' is not a valid {DataType} for setting {Key}.");
        }

        Value = trimmed;
    }

    public static bool IsValidValue(SettingDataType type, string value) => type switch
    {
        SettingDataType.String => true,
        SettingDataType.Int => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        SettingDataType.Decimal => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
        SettingDataType.Bool => bool.TryParse(value, out _),
        SettingDataType.Time => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        SettingDataType.Json => IsJson(value),
        _ => false,
    };

    private static bool IsJson(string value)
    {
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(value);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
