using System.Windows;
using System.Windows.Controls;

namespace HotelPOS.Desktop.Modules.Reports;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    // Report columns are generated from the data; amounts and counts are right-aligned so their digits line up.
    private void OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (DataContext is ReportsViewModel vm && vm.NumericColumns.Contains(e.PropertyName) && e.Column is DataGridTextColumn text)
        {
            text.ElementStyle = (Style)FindResource("Text.GridNumber");
            text.HeaderStyle = (Style)FindResource("DataGrid.NumberHeader");
        }
    }
}
