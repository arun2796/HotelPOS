using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Print;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Printing;

public sealed partial class PrinterProfileEditor : ObservableObject
{
    public PrinterProfileEditor(PrintDocumentType type, PrinterProfile? profile, bool autoPrint)
    {
        Type = type;
        _isEnabled = profile is not null;
        _autoPrint = autoPrint;
        var p = profile ?? new PrinterProfile();
        _kind = p.Kind;
        _printerName = p.PrinterName;
        _host = p.Host;
        _port = p.Port.ToString(CultureInfo.InvariantCulture);
        _paperWidth = p.PaperWidthMm <= 58 ? 58 : 80;
        _copies = Math.Clamp(p.Copies, 1, PrintLimits.MaxCopies);
        _model = p.Model;
        _currencyFallback = p.CurrencyFallback ?? string.Empty;
    }

    public PrintDocumentType Type { get; }

    public string Title => DocumentPrinter.Label(Type) + (Type == PrintDocumentType.Kot ? " (kitchen)" : string.Empty);

    public string AutoPrintText => Type switch
    {
        PrintDocumentType.Kot => "Print automatically when a ticket is created (kitchen terminals)",
        PrintDocumentType.Invoice => "Print automatically when the invoice is finalized",
        _ => "Print automatically after every payment",
    };

    public IReadOnlyList<PrinterKind> Kinds { get; } = Enum.GetValues<PrinterKind>();

    public IReadOnlyList<PrinterModel> Models { get; } = Enum.GetValues<PrinterModel>();

    public IReadOnlyList<int> PaperWidths { get; } = new[] { 58, 80 };

    public IReadOnlyList<int> CopyOptions { get; } = Enumerable.Range(1, PrintLimits.MaxCopies).ToList();

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _autoPrint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNetwork), nameof(IsSpooled), nameof(IsThermal))]
    private PrinterKind _kind;

    [ObservableProperty]
    private string _printerName;

    [ObservableProperty]
    private string _host;

    [ObservableProperty]
    private string _port;

    [ObservableProperty]
    private int _paperWidth;

    [ObservableProperty]
    private int _copies;

    [ObservableProperty]
    private PrinterModel _model;

    [ObservableProperty]
    private string _currencyFallback;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string? _testResult;

    [ObservableProperty]
    private bool _isTesting;

    public bool IsNetwork => Kind == PrinterKind.Network;

    public bool IsSpooled => Kind != PrinterKind.Network;

    public bool IsThermal => Kind != PrinterKind.Windows;

    public PrinterProfile? Build(out string? error)
    {
        error = null;
        if (!IsEnabled)
        {
            return null;
        }

        if (!int.TryParse(Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            error = "Enter a port between 1 and 65535 (thermal printers usually use 9100).";
            return null;
        }

        var profile = new PrinterProfile
        {
            Kind = Kind,
            PrinterName = PrinterName.Trim(),
            Host = Host.Trim(),
            Port = port,
            PaperWidthMm = PaperWidth,
            Copies = Math.Clamp(Copies, 1, PrintLimits.MaxCopies),
            Model = Model,
            CurrencyFallback = string.IsNullOrWhiteSpace(CurrencyFallback) ? null : CurrencyFallback.Trim(),
        };
        if (!PrinterProfiles.IsUsable(profile))
        {
            error = Kind == PrinterKind.Network ? "Enter the printer's network address." : "Choose or type the Windows printer name.";
            return null;
        }

        return profile;
    }
}

public sealed partial class PrinterSettingsViewModel : ObservableObject, INavigationAware
{
    private readonly IClientSettingsService _settings;
    private readonly IDocumentPrinter _printer;
    private readonly IWindowsPrinterChannel _windows;
    private readonly INotificationService _notifications;

    public PrinterSettingsViewModel(IClientSettingsService settings, IDocumentPrinter printer, IWindowsPrinterChannel windows, INotificationService notifications)
    {
        _settings = settings;
        _printer = printer;
        _windows = windows;
        _notifications = notifications;
    }

    public ObservableCollection<PrinterProfileEditor> Profiles { get; } = new();

    public ObservableCollection<string> InstalledPrinters { get; } = new();

    [ObservableProperty]
    private string? _errorMessage;

    public string DeviceName => _settings.Current.DeviceName;

    public Task OnNavigatedToAsync(object? parameter)
    {
        var current = _settings.Current;
        Profiles.Clear();
        foreach (var type in new[] { PrintDocumentType.Kot, PrintDocumentType.Invoice, PrintDocumentType.Receipt })
        {
            Profiles.Add(new PrinterProfileEditor(type, current.Printers.GetValueOrDefault(type), AutoPrintOf(current, type)));
        }

        InstalledPrinters.Clear();
        foreach (var name in _windows.InstalledPrinters())
        {
            InstalledPrinters.Add(name);
        }

        return Task.CompletedTask;
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;
        var updated = _settings.Current;
        foreach (var editor in Profiles)
        {
            var profile = editor.Build(out var error);
            editor.Error = error;
            if (error is not null)
            {
                ErrorMessage = "Fix the highlighted printer before saving.";
                return;
            }

            if (profile is null)
            {
                updated.Printers.Remove(editor.Type);
            }
            else
            {
                updated.Printers[editor.Type] = profile;
            }

            switch (editor.Type)
            {
                case PrintDocumentType.Kot:
                    updated.AutoPrintKot = editor.AutoPrint;
                    break;
                case PrintDocumentType.Invoice:
                    updated.AutoPrintInvoice = editor.AutoPrint;
                    break;
                default:
                    updated.AutoPrintReceipt = editor.AutoPrint;
                    break;
            }
        }

        _settings.Save(updated);
        _notifications.Success("Printer settings saved for this terminal.");
    }

    [RelayCommand]
    private async Task TestPrintAsync(PrinterProfileEditor? editor)
    {
        if (editor is null || editor.IsTesting)
        {
            return;
        }

        var profile = editor.Build(out var error);
        editor.Error = error;
        if (profile is null)
        {
            editor.TestResult = error ?? "Enable the printer first.";
            return;
        }

        editor.IsTesting = true;
        editor.TestResult = "Printing…";
        try
        {
            await _printer.TestPrintAsync(editor.Type, profile);
            editor.TestResult = $"Test page sent to {profile.Target}.";
        }
        catch (PrintException ex)
        {
            editor.TestResult = ex.Message;
        }
        finally
        {
            editor.IsTesting = false;
        }
    }

    private static bool AutoPrintOf(ClientSettings settings, PrintDocumentType type) => type switch
    {
        PrintDocumentType.Kot => settings.AutoPrintKot,
        PrintDocumentType.Invoice => settings.AutoPrintInvoice,
        _ => settings.AutoPrintReceipt,
    };
}
