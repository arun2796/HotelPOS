using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Admin;

public sealed record SettingDto
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public SettingDataType DataType { get; init; }
    public string? Description { get; init; }
    public bool IsPublic { get; init; }
}

public sealed record SettingValue
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed record UpdateSettingsRequest
{
    public IReadOnlyList<SettingValue> Items { get; init; } = Array.Empty<SettingValue>();
}

public sealed record SystemInfoDto
{
    public string RestaurantName { get; init; } = string.Empty;
    public string ApiVersion { get; init; } = string.Empty;
    public string MinClientVersion { get; init; } = string.Empty;
    public DateTime ServerTimeUtc { get; init; }
}

public static class SettingKeys
{
    public const string RestaurantName = "RestaurantName";
    public const string Address = "Address";
    public const string Phone = "Phone";
    public const string Gstin = "Gstin";
    public const string CurrencySymbol = "CurrencySymbol";
    public const string InvoicePrefix = "InvoicePrefix";
    public const string InvoiceResetPolicy = "InvoiceResetPolicy";
    public const string RoundOffTotals = "RoundOffTotals";
    public const string BusinessDayStartTime = "BusinessDayStartTime";
    public const string KitchenWarnMinutes = "KitchenWarnMinutes";
    public const string KitchenLateMinutes = "KitchenLateMinutes";
    public const string AutoCloseOnFullPayment = "AutoCloseOnFullPayment";
    public const string ReceiptFooter = "ReceiptFooter";
    public const string MenuVersion = "MenuVersion";
    public const string TaxSplitDisplay = "TaxSplitDisplay";
    public const string AllowAnyWaiterToEditOrders = "AllowAnyWaiterToEditOrders";
    public const string MaxCashierDiscountPercent = "MaxCashierDiscountPercent";
    public const string AllowBillBeforeReady = "AllowBillBeforeReady";
}
