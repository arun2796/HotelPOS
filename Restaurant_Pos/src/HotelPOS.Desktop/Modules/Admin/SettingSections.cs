using System.Collections.ObjectModel;
using HotelPOS.Contracts.Admin;

namespace HotelPOS.Desktop.Modules.Admin;

// The settings list is long and the server returns it flat; these are the headings it is shown under, in this order.
// A key not listed here goes under "Other", so a new server setting still appears.
public static class SettingSections
{
    public const string Notifications = "Notifications";

    // Section names a deep link can ask for (Phase 21 A7): NavigateToAsync(ModuleRegistry.Settings, SettingSections.Restaurant).
    public const string Restaurant = "Restaurant";
    public const string BusinessDay = "Business day";
    public const string OrdersAndBilling = "Orders and billing";
    public const string InvoicesAndPrinting = "Invoices and printing";

    private static readonly (string Section, string[] Keys)[] Layout =
    {
        (Restaurant, new[] { SettingKeys.RestaurantName, SettingKeys.Address, SettingKeys.Phone, SettingKeys.Gstin, SettingKeys.LogoFile, SettingKeys.CurrencySymbol, SettingKeys.DefaultSacCode }),
        (BusinessDay, new[] { SettingKeys.BusinessDayStartTime, SettingKeys.DayEndReminderTime, SettingKeys.AutoCloseOnFullPayment }),
        (OrdersAndBilling, new[] { SettingKeys.AllowAnyWaiterToEditOrders, SettingKeys.AllowBillBeforeReady, SettingKeys.MaxCashierDiscountPercent, SettingKeys.RoundOffTotals, SettingKeys.TaxSplitDisplay, SettingKeys.UpiVpa, SettingKeys.UpiPayeeName }),
        (InvoicesAndPrinting, new[] { SettingKeys.InvoicePrefix, SettingKeys.InvoiceResetPolicy, SettingKeys.PrintInvoiceCopies, SettingKeys.ReceiptFooter, SettingKeys.KotShowPrices, SettingKeys.PdfPaper }),
        ("Kitchen", new[] { SettingKeys.KitchenWarnMinutes, SettingKeys.KitchenLateMinutes }),
        ("Cash", new[] { SettingKeys.CashVarianceApprovalLimit, SettingKeys.CashPayOutApprovalLimit, SettingKeys.CashBlindCount }),
        ("Security", new[] { SettingKeys.LockoutThreshold, SettingKeys.LockoutMinutes, SettingKeys.PasswordMinLength, SettingKeys.PasswordRequireLetter, SettingKeys.PasswordRequireDigit, SettingKeys.PasswordRequireUpperAndLower, SettingKeys.PasswordRequireSymbol, SettingKeys.AuditRetentionDays }),
        ("Backup", new[] { SettingKeys.BackupEnabled, SettingKeys.BackupTime, SettingKeys.BackupRetentionDays, SettingKeys.BackupPath }),
    };

    private const string Other = "Other";

    public static string Of(string key) => Layout.FirstOrDefault(s => s.Keys.Contains(key)).Section ?? Other;

    // Groups in layout order, each with its items in layout order; "Other" last; empty groups left out.
    public static IReadOnlyList<SettingGroupViewModel> Group(IEnumerable<SettingItemViewModel> items)
    {
        var list = items.ToList();
        var groups = new List<SettingGroupViewModel>();
        foreach (var (section, keys) in Layout)
        {
            var members = keys.Select(k => list.FirstOrDefault(i => i.Key == k)).OfType<SettingItemViewModel>().ToList();
            if (members.Count > 0)
            {
                groups.Add(new SettingGroupViewModel(section, members));
            }
        }

        var rest = list.Where(i => Of(i.Key) == Other).ToList();
        if (rest.Count > 0)
        {
            groups.Add(new SettingGroupViewModel(Other, rest));
        }

        return groups;
    }
}

public sealed class SettingGroupViewModel
{
    public SettingGroupViewModel(string name, IReadOnlyList<SettingItemViewModel> items)
    {
        Name = name;
        Items = new ObservableCollection<SettingItemViewModel>(items);
    }

    public string Name { get; }

    public ObservableCollection<SettingItemViewModel> Items { get; }
}
