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

    // The newest terminal installer this server hosts under /updates/; null when none was published.
    public ClientUpdateInfo? ClientUpdate { get; init; }
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
    public const string DayEndReminderTime = "DayEndReminderTime";
    public const string KitchenLateMinutes = "KitchenLateMinutes";
    public const string AutoCloseOnFullPayment = "AutoCloseOnFullPayment";
    public const string ReceiptFooter = "ReceiptFooter";
    public const string MenuVersion = "MenuVersion";
    public const string TaxSplitDisplay = "TaxSplitDisplay";
    public const string AllowAnyWaiterToEditOrders = "AllowAnyWaiterToEditOrders";
    public const string MaxCashierDiscountPercent = "MaxCashierDiscountPercent";
    public const string AllowBillBeforeReady = "AllowBillBeforeReady";
    public const string PrintInvoiceCopies = "PrintInvoiceCopies";
    public const string KotShowPrices = "KotShowPrices";
    public const string LockoutThreshold = "LockoutThreshold";
    public const string AuditRetentionDays = "AuditRetentionDays";
    public const string LockoutMinutes = "LockoutMinutes";

    // Password rule (Phase 9): minimum length (8 to 100) and which kinds of characters are required.
    public const string PasswordMinLength = "PasswordMinLength";
    public const string PasswordRequireLetter = "PasswordRequireLetter";
    public const string PasswordRequireDigit = "PasswordRequireDigit";
    public const string PasswordRequireUpperAndLower = "PasswordRequireUpperAndLower";
    public const string PasswordRequireSymbol = "PasswordRequireSymbol";

    // Phase 14: print templates and PDFs.
    public const string UpiVpa = "UpiVpa";
    public const string UpiPayeeName = "UpiPayeeName";
    public const string LogoFile = "LogoFile";
    public const string PdfPaper = "PdfPaper";
    public const string DefaultSacCode = "DefaultSacCode";

    // Phase 15: GST returns. Inter-state B2C invoices above this value are B2CL (GSTR-1 table 5).
    public const string GstB2clThreshold = "GstB2clThreshold";

    public const string BackupEnabled = "BackupEnabled";
    public const string BackupTime = "BackupTime";
    public const string BackupRetentionDays = "BackupRetentionDays";
    public const string BackupPath = "BackupPath";

    public const string CashVarianceApprovalLimit = "CashVarianceApprovalLimit";
    public const string CashPayOutApprovalLimit = "CashPayOutApprovalLimit";
    public const string CashBlindCount = "CashBlindCount";
}
