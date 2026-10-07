using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Printing;

namespace HotelPOS.Desktop.Modules.Printing;

public sealed record PrintPreviewContext(PrintJob Job, string ReturnModule, bool CanPrint);

public sealed partial class PrintPreviewViewModel : ObservableObject, INavigationAware
{
    private readonly FlowDocumentRenderer _renderer;
    private readonly IDocumentPrinter _printer;
    private readonly INavigationService _navigation;
    private PrintPreviewContext? _context;

    public PrintPreviewViewModel(FlowDocumentRenderer renderer, IDocumentPrinter printer, INavigationService navigation)
    {
        _renderer = renderer;
        _printer = printer;
        _navigation = navigation;
    }

    [ObservableProperty]
    private FlowDocument? _document;

    [ObservableProperty]
    private string _title = "Preview";

    [ObservableProperty]
    private bool _canPrint;

    [ObservableProperty]
    private string _printerText = string.Empty;

    public Task OnNavigatedToAsync(object? parameter)
    {
        if (parameter is PrintPreviewContext context)
        {
            _context = context;
            Title = context.Job.Title;
            CanPrint = context.CanPrint;
            PrinterText = context.CanPrint ? $"Prints on {context.Job.Profile.Target}" : "No printer is set up for this document on this terminal.";
            Document = _renderer.Render(context.Job.Document, context.Job.Profile);
        }

        return Task.CompletedTask;
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private void Print()
    {
        if (_context is { CanPrint: true })
        {
            _printer.Print(_context.Job with { NotifyOnSuccess = true });
        }
    }

    [RelayCommand]
    private Task CloseAsync() => _navigation.NavigateToAsync(_context?.ReturnModule ?? ModuleRegistry.Billing);
}
