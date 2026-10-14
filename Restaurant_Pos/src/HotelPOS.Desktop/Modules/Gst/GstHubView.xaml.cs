using System.Windows;
using System.Windows.Controls;

namespace HotelPOS.Desktop.Modules.Gst;

public partial class GstHubView : UserControl
{
    public GstHubView()
    {
        InitializeComponent();
    }

    // Amounts and counts are right-aligned so their digits line up.
    private void OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (DataContext is GstHubViewModel vm && vm.NumericColumns.Contains(e.PropertyName) && e.Column is DataGridTextColumn text)
        {
            text.ElementStyle = (Style)FindResource("Text.GridNumber");
            text.HeaderStyle = (Style)FindResource("DataGrid.NumberHeader");
        }
    }
}
