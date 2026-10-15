using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

public sealed record Choice<T>(T Value, string Label);

public sealed record PreviewSource(int? BillId, string Label);

public sealed partial class TemplateColumnOption : ObservableObject
{
    public TemplateColumnOption(TemplateColumn column, string label, bool isIncluded)
    {
        Column = column;
        Label = label;
        _isIncluded = isIncluded;
    }

    public TemplateColumn Column { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isIncluded;
}

public sealed record TemplateVersionRow(DocumentTemplateDto Template)
{
    public string Title => $"v{Template.Version.ToString(CultureInfo.InvariantCulture)} · {Template.Name}";

    public string Detail => Template.IsActive
        ? $"Active since {Local(Template.ActivatedAtUtc ?? Template.CreatedAtUtc)}{By(Template.ActivatedBy)}"
        : $"Saved {Local(Template.CreatedAtUtc)}{By(Template.CreatedBy)}";

    public bool IsActive => Template.IsActive;

    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture);

    private static string By(string? name) => string.IsNullOrWhiteSpace(name) ? string.Empty : $" by {name}";
}

// Template Studio (Settings hub, "Print templates"): the layout options of a document type on the left, a live preview
// of the server-built render model on the right (sample data or a real closed bill), versions with activate / roll
// back, and a test print on this terminal's printer.
public sealed partial class TemplateStudioViewModel : ViewModelBase
{
    private static readonly IReadOnlyDictionary<TemplateColumn, string> ColumnLabels = new Dictionary<TemplateColumn, string>
    {
        [TemplateColumn.Item] = "Item",
        [TemplateColumn.HsnSac] = "HSN / SAC",
        [TemplateColumn.Quantity] = "Quantity",
        [TemplateColumn.Rate] = "Rate",
        [TemplateColumn.Discount] = "Discount",
        [TemplateColumn.TaxPercent] = "GST %",
        [TemplateColumn.Amount] = "Amount",
    };

    private readonly ITemplatesApi _api;
    private readonly IBillingApi _billing;
    private readonly IDocumentPrinter _printer;
    private readonly IPrinterProfiles _profiles;
    private readonly IFilePicker _files;
    private readonly INotificationService _notifications;
    private readonly IRealtimeClient _realtime;
    private List<DocumentTemplateDto> _all = new();
    private object? _previewModel;
    private bool _loadingLayout;
    private bool _keepEditor;
    private int _previewRequest;
    private IDisposable? _subscription;

    public TemplateStudioViewModel(
        ITemplatesApi api,
        IBillingApi billing,
        IDocumentPrinter printer,
        IPrinterProfiles profiles,
        IFilePicker files,
        INotificationService notifications,
        IRealtimeClient realtime)
    {
        _api = api;
        _billing = billing;
        _printer = printer;
        _profiles = profiles;
        _files = files;
        _notifications = notifications;
        _realtime = realtime;
        _selectedType = DocumentTypes[1];
        _selectedSource = Sources[0];
        LoadLayout(TemplateLayout.DefaultFor(PrintDocumentType.Invoice));
    }

    public IReadOnlyList<Choice<PrintDocumentType>> DocumentTypes { get; } = new[]
    {
        new Choice<PrintDocumentType>(PrintDocumentType.Kot, "Kitchen ticket"),
        new Choice<PrintDocumentType>(PrintDocumentType.Invoice, "Invoice"),
        new Choice<PrintDocumentType>(PrintDocumentType.Receipt, "Receipt"),
    };

    public IReadOnlyList<Choice<PaperSize>> Papers { get; } = new[]
    {
        new Choice<PaperSize>(PaperSize.Mm58, "58 mm roll"),
        new Choice<PaperSize>(PaperSize.Mm80, "80 mm roll"),
        new Choice<PaperSize>(PaperSize.A5, "A5 sheet"),
        new Choice<PaperSize>(PaperSize.A4, "A4 sheet"),
    };

    public IReadOnlyList<Choice<GstBreakupStyle>> GstStyles { get; } = new[]
    {
        new Choice<GstBreakupStyle>(GstBreakupStyle.FollowSetting, "As the tax setting says"),
        new Choice<GstBreakupStyle>(GstBreakupStyle.SplitRows, "CGST and SGST rows"),
        new Choice<GstBreakupStyle>(GstBreakupStyle.RateTable, "Rate-wise table"),
        new Choice<GstBreakupStyle>(GstBreakupStyle.SingleLine, "One GST line"),
    };

    public IReadOnlyList<Choice<bool?>> KotPriceChoices { get; } = new[]
    {
        new Choice<bool?>(null, "As the KOT prices setting says"),
        new Choice<bool?>(true, "Show prices"),
        new Choice<bool?>(false, "Hide prices"),
    };

    public ObservableCollection<PreviewSource> Sources { get; } = new() { new PreviewSource(null, "Sample bill") };

    public ObservableCollection<TemplateVersionRow> Versions { get; } = new();

    public ObservableCollection<TemplateColumnOption> Columns { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInvoice), nameof(IsKot), nameof(IsBillDocument))]
    private Choice<PrintDocumentType> _selectedType;

    [ObservableProperty]
    private TemplateVersionRow? _selectedVersion;

    [ObservableProperty]
    private PreviewSource _selectedSource;

    [ObservableProperty]
    private Choice<PaperSize>? _paper;

    [ObservableProperty]
    private Choice<GstBreakupStyle>? _gstStyle;

    [ObservableProperty]
    private Choice<bool?>? _kotPrices;

    [ObservableProperty]
    private bool _showLogo;

    [ObservableProperty]
    private bool _showUpiQr;

    [ObservableProperty]
    private bool _largeText;

    [ObservableProperty]
    private bool _showAddress;

    [ObservableProperty]
    private bool _showPhone;

    [ObservableProperty]
    private bool _showGstin;

    [ObservableProperty]
    private bool _showWaiter;

    [ObservableProperty]
    private bool _showGuests;

    [ObservableProperty]
    private bool _showCustomer;

    [ObservableProperty]
    private bool _showPayments;

    [ObservableProperty]
    private bool _showPrintedAt;

    [ObservableProperty]
    private bool _kotNotesBold;

    [ObservableProperty]
    private bool _kotStationHeading;

    [ObservableProperty]
    private string _headerText = string.Empty;

    [ObservableProperty]
    private string _footerText = string.Empty;

    [ObservableProperty]
    private string _termsText = string.Empty;

    [ObservableProperty]
    private string _versionName = string.Empty;

    [ObservableProperty]
    private TemplateColumnOption? _selectedColumn;

    [ObservableProperty]
    private FlowDocument? _preview;

    [ObservableProperty]
    private string _previewCaption = string.Empty;

    public bool IsInvoice => SelectedType.Value == PrintDocumentType.Invoice;

    public bool IsKot => SelectedType.Value == PrintDocumentType.Kot;

    public bool IsBillDocument => !IsKot;

    public string Title => "Print templates";

    public async Task LoadAsync()
    {
        _subscription ??= _realtime.Subscribe<TemplateActivatedEvent>(HubEvents.TemplateActivated, e => _ = LoadVersionsAsync(keepEditor: true));
        await LoadVersionsAsync(keepEditor: false);
        await LoadSourcesAsync();
        await PreviewAsync();
    }

    public void Unload()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    // The layout the options describe, as the server will store it.
    public TemplateLayout BuildLayout() => new()
    {
        Paper = Paper?.Value ?? PaperSize.Mm80,
        ShowLogo = ShowLogo,
        HeaderLines = Lines(HeaderText),
        FooterLines = Lines(FooterText),
        Terms = Lines(TermsText),
        Columns = Columns.Where(c => c.IsIncluded).Select(c => c.Column).ToList(),
        GstBreakup = GstStyle?.Value ?? GstBreakupStyle.FollowSetting,
        ShowUpiQr = ShowUpiQr,
        FontSize = LargeText ? TemplateFontSize.Large : TemplateFontSize.Normal,
        ShowAddress = ShowAddress,
        ShowPhone = ShowPhone,
        ShowGstin = ShowGstin,
        ShowWaiter = ShowWaiter,
        ShowGuests = ShowGuests,
        ShowCustomer = ShowCustomer,
        ShowPayments = ShowPayments,
        ShowPrintedAt = ShowPrintedAt,
        KotShowPrices = KotPrices?.Value,
        KotNotesBold = KotNotesBold,
        KotStationHeading = KotStationHeading,
    };

    public void LoadLayout(TemplateLayout layout)
    {
        _loadingLayout = true;
        try
        {
            Paper = Papers.FirstOrDefault(p => p.Value == layout.Paper) ?? Papers[1];
            GstStyle = GstStyles.FirstOrDefault(g => g.Value == layout.GstBreakup) ?? GstStyles[0];
            KotPrices = KotPriceChoices.First(k => k.Value == layout.KotShowPrices);
            ShowLogo = layout.ShowLogo;
            ShowUpiQr = layout.ShowUpiQr;
            LargeText = layout.FontSize == TemplateFontSize.Large;
            ShowAddress = layout.ShowAddress;
            ShowPhone = layout.ShowPhone;
            ShowGstin = layout.ShowGstin;
            ShowWaiter = layout.ShowWaiter;
            ShowGuests = layout.ShowGuests;
            ShowCustomer = layout.ShowCustomer;
            ShowPayments = layout.ShowPayments;
            ShowPrintedAt = layout.ShowPrintedAt;
            KotNotesBold = layout.KotNotesBold;
            KotStationHeading = layout.KotStationHeading;
            HeaderText = string.Join(Environment.NewLine, layout.HeaderLines);
            FooterText = string.Join(Environment.NewLine, layout.FooterLines);
            TermsText = string.Join(Environment.NewLine, layout.Terms);

            foreach (var column in Columns)
            {
                column.PropertyChanged -= OnColumnChanged;
            }

            Columns.Clear();
            var included = layout.Columns.Where(c => ColumnLabels.ContainsKey(c)).Distinct().ToList();
            foreach (var column in included.Concat(ColumnLabels.Keys.Except(included)))
            {
                var option = new TemplateColumnOption(column, ColumnLabels[column], included.Contains(column));
                option.PropertyChanged += OnColumnChanged;
                Columns.Add(option);
            }
        }
        finally
        {
            _loadingLayout = false;
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loadingLayout && e.PropertyName is nameof(Paper) or nameof(GstStyle) or nameof(KotPrices) or nameof(ShowLogo) or nameof(ShowUpiQr)
            or nameof(LargeText) or nameof(ShowAddress) or nameof(ShowPhone) or nameof(ShowGstin) or nameof(ShowWaiter) or nameof(ShowGuests)
            or nameof(ShowCustomer) or nameof(ShowPayments) or nameof(ShowPrintedAt) or nameof(KotNotesBold) or nameof(KotStationHeading)
            or nameof(HeaderText) or nameof(FooterText) or nameof(TermsText) or nameof(SelectedSource))
        {
            _ = PreviewAsync();
        }
    }

    partial void OnSelectedTypeChanged(Choice<PrintDocumentType> value)
    {
        SelectedSource = Sources[0];
        ShowVersions(keepEditor: false);
        _ = PreviewAsync();
    }

    partial void OnSelectedVersionChanged(TemplateVersionRow? value)
    {
        if (value is not null && !_keepEditor)
        {
            LoadLayout(value.Template.Layout);
            VersionName = value.Template.Name;
            _ = PreviewAsync();
        }
    }

    [RelayCommand]
    private void MoveColumnUp(TemplateColumnOption? column) => Move(column, -1);

    [RelayCommand]
    private void MoveColumnDown(TemplateColumnOption? column) => Move(column, 1);

    [RelayCommand]
    public async Task PreviewAsync()
    {
        var request = ++_previewRequest;
        var type = SelectedType.Value;
        var result = await _api.PreviewAsync(new TemplatePreviewRequest
        {
            DocumentType = type,
            Layout = BuildLayout(),
            SourceId = await SourceIdAsync(type),
        });
        if (request != _previewRequest)
        {
            return;
        }

        if (!result.Success || result.Data is null)
        {
            ErrorMessage = result.IsConnectionFailure ? result.Message : ApiFailures.Describe(result);
            return;
        }

        ErrorMessage = null;
        _previewModel = (object?)result.Data.Invoice ?? (object?)result.Data.Receipt ?? result.Data.Kot;
        if (_previewModel is null)
        {
            return;
        }

        var layout = BuildLayout();
        var profile = new PrinterProfile
        {
            Kind = PrinterKind.Windows,
            PaperWidthMm = layout.Paper == PaperSize.Mm58 ? 58 : 80,
            CurrencyFallback = _profiles.For(type)?.CurrencyFallback ?? "Rs.",
        };
        Preview = FlowDocumentRenderer.Render(EscPosRenderer.Layout(_previewModel, EscPosRenderer.ColumnsFor(profile, layout), profile.CurrencyFallback),
            EscPosRenderer.ColumnsFor(profile, layout));
        PreviewCaption = (result.Data.IsSample ? "Sample data" : SelectedSource.Label) + " · " + (Paper?.Label ?? string.Empty);
    }

    [RelayCommand]
    private Task SaveAsync() => SaveVersionAsync(activate: false);

    [RelayCommand]
    private Task SaveAndActivateAsync() => SaveVersionAsync(activate: true);

    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (SelectedVersion is not { } version || version.IsActive)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _api.ActivateAsync(SelectedType.Value, version.Template.Version);
            if (!result.Success)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            _notifications.Success($"{SelectedType.Label} template v{version.Template.Version} is active; the next print uses it.");
            await LoadVersionsAsync(keepEditor: true);
        });
    }

    [RelayCommand]
    private void PrintTest()
    {
        var type = SelectedType.Value;
        if (_previewModel is null)
        {
            return;
        }

        if (_profiles.For(type) is not { } profile)
        {
            _notifications.Warning($"No {SelectedType.Label.ToLowerInvariant()} printer is set up on this terminal (Printers in the menu).");
            return;
        }

        // A test print of the preview: no document id, so no copy number is taken.
        _printer.Print(new PrintJob(type, 0, $"{SelectedType.Label} template test", _previewModel, profile, 1) { NotifyOnSuccess = true });
    }

    [RelayCommand]
    private async Task UploadLogoAsync()
    {
        var path = _files.PickImage();
        if (path is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            await using var stream = File.OpenRead(path);
            var result = await _api.UploadLogoAsync(stream, Path.GetFileName(path));
            if (!result.Success)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            _notifications.Success("Logo saved. Templates with \"Logo\" on print it.");
            ShowLogo = true;
        });
    }

    [RelayCommand]
    private async Task RemoveLogoAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _api.RemoveLogoAsync();
            if (!result.Success)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            _notifications.Success("Logo removed.");
            await PreviewAsync();
        });
    }

    private async Task SaveVersionAsync(bool activate)
    {
        var name = string.IsNullOrWhiteSpace(VersionName) ? $"{SelectedType.Label} {DateTime.Now:dd MMM HH:mm}" : VersionName.Trim();
        await ExecuteAsync(async () =>
        {
            var result = await _api.SaveAsync(SelectedType.Value, new SaveTemplateRequest { Name = name, Layout = BuildLayout(), Activate = activate });
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.IsConnectionFailure ? "Connection unavailable. The template was NOT saved." : ApiFailures.Describe(result);
                return;
            }

            _notifications.Success(activate
                ? $"Saved as v{result.Data.Version} and activated; the next print uses it."
                : $"Saved as v{result.Data.Version}. Activate it to print with it.");
            await LoadVersionsAsync(keepEditor: true);
            SelectedVersion = Versions.FirstOrDefault(v => v.Template.Version == result.Data.Version);
        });
    }

    private async Task LoadVersionsAsync(bool keepEditor)
    {
        var all = await FetchAsync(_api.ListAsync());
        if (all is null)
        {
            return;
        }

        _all = all;
        ShowVersions(keepEditor);
    }

    private void ShowVersions(bool keepEditor)
    {
        var selected = SelectedVersion?.Template.Version;
        Versions.Clear();
        foreach (var template in _all.Where(t => t.DocumentType == SelectedType.Value).OrderByDescending(t => t.Version))
        {
            Versions.Add(new TemplateVersionRow(template));
        }

        if (keepEditor)
        {
            _keepEditor = true;
            SelectedVersion = Versions.FirstOrDefault(v => v.Template.Version == selected);
            _keepEditor = false;
            return;
        }

        SelectedVersion = Versions.FirstOrDefault(v => v.IsActive) ?? Versions.FirstOrDefault();
        if (SelectedVersion is null)
        {
            LoadLayout(TemplateLayout.DefaultFor(SelectedType.Value));
        }
    }

    private async Task LoadSourcesAsync()
    {
        var bills = await _billing.GetClosedAsync(null, null);
        while (Sources.Count > 1)
        {
            Sources.RemoveAt(1);
        }

        foreach (var bill in (bills.Data ?? new List<BillSummaryDto>()).Take(20))
        {
            Sources.Add(new PreviewSource(bill.Id, $"{bill.InvoiceNumber ?? "Bill " + bill.BillNumber} · Table {bill.TableCode}"));
        }
    }

    // A real bill previews the invoice itself, its first payment for a receipt; kitchen tickets preview sample data.
    private async Task<int?> SourceIdAsync(PrintDocumentType type)
    {
        if (SelectedSource.BillId is not { } billId || type == PrintDocumentType.Kot)
        {
            return null;
        }

        if (type == PrintDocumentType.Invoice)
        {
            return billId;
        }

        var bill = await _billing.GetAsync(billId);
        return bill.Data?.Payments.FirstOrDefault()?.Id;
    }

    private void OnColumnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_loadingLayout)
        {
            _ = PreviewAsync();
        }
    }

    private void Move(TemplateColumnOption? column, int step)
    {
        if (column is null)
        {
            return;
        }

        var index = Columns.IndexOf(column);
        var target = index + step;
        if (index < 0 || target < 0 || target >= Columns.Count)
        {
            return;
        }

        Columns.Move(index, target);
        SelectedColumn = column;
        _ = PreviewAsync();
    }

    private static IReadOnlyList<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim('\r', ' ')).Where(l => l.Length > 0).ToList();
}
