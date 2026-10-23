using System.Windows;
using System.Windows.Controls;

namespace HotelPOS.Desktop.Modules.Gst;

public partial class GstHubView : UserControl
{
    public GstHubView()
    {
        InitializeComponent();
    }

    // Headers in capitals like every other grid (the table names columns "Invoice number", "Igst"; capitals also put
    // the acronyms right: IGST, CGST, SGST, GSTIN, HSN/SAC). Amounts and counts are right-aligned so their digits line up.
    private void OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        e.Column.Header = e.PropertyName.ToUpperInvariant();
        if (DataContext is GstHubViewModel vm && vm.NumericColumns.Contains(e.PropertyName) && e.Column is DataGridTextColumn text)
        {
            text.ElementStyle = (Style)FindResource("Text.GridNumber");
            text.HeaderStyle = (Style)FindResource("DataGrid.NumberHeader");
        }
    }
}
