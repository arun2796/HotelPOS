using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace HotelPOS.Desktop.Startup;

// One culture for every screen, whatever the Windows regional settings: Indian digit grouping (₹1,23,456.00),
// dd-MM-yyyy dates and 24-hour times.
public static class IndianCulture
{
    private static bool _applied;

    public static CultureInfo Create()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-IN").Clone();
        var numbers = culture.NumberFormat;
        numbers.CurrencySymbol = "₹";
        numbers.NumberGroupSizes = new[] { 3, 2 };
        numbers.CurrencyGroupSizes = new[] { 3, 2 };
        numbers.NumberDecimalSeparator = numbers.CurrencyDecimalSeparator = ".";
        numbers.NumberGroupSeparator = numbers.CurrencyGroupSeparator = ",";
        var dates = culture.DateTimeFormat;
        dates.DateSeparator = "-";
        dates.ShortDatePattern = "dd-MM-yyyy";
        dates.ShortTimePattern = "HH:mm";
        dates.LongTimePattern = "HH:mm:ss";
        return culture;
    }

    public static void Apply()
    {
        var culture = Create();
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // WPF formats bindings with the element language, not the thread culture; set it once per process.
        if (!_applied)
        {
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
            _applied = true;
        }
    }
}
