using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace HotelPOS.Desktop.Modules.Admin;

public partial class TemplateStudioView : UserControl
{
    private TemplateStudioViewModel? _vm;

    public TemplateStudioView()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    // A FlowDocument can belong to one viewer only, so the document is handed over while this view is on screen.
    private void Attach()
    {
        Detach();
        _vm = DataContext as TemplateStudioViewModel;
        if (_vm is null)
        {
            return;
        }

        _vm.PropertyChanged += OnChanged;
        PreviewViewer.Document = _vm.Preview;
    }

    private void Detach()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnChanged;
            _vm = null;
        }

        PreviewViewer.Document = null;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TemplateStudioViewModel.Preview) && _vm is not null)
        {
            PreviewViewer.Document = _vm.Preview;
        }
    }
}
