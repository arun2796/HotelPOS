using System.Windows;
using System.Windows.Controls;

namespace HotelPOS.Desktop.Modules.Dashboard;

public partial class DashboardView : UserControl
{
    /// <summary>Content width under which the dashboard drops to two columns (Phase 21 A8: 1366×768 with the sidebar open).</summary>
    public const double CompactWidth = 1280;

    public DashboardView()
    {
        InitializeComponent();
    }

    // Five KPI cards (Phase 22 F4 added "Expenses today") shrink gracefully (the value is a DownOnly Viewbox) down to
    // about 225 px each, so one row needs ~1150 px; below that they go 3 + 2, and under 700 px they pair up.
    public static int KpiColumnsFor(double width) => width < 700 ? 2 : width < 1150 ? 3 : 5;

    public static int PanelColumnsFor(double width) => width < CompactWidth ? 2 : 3;

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        KpiRow.Columns = KpiColumnsFor(e.NewSize.Width);
        PanelRow.Columns = PanelColumnsFor(e.NewSize.Width);
    }
}
