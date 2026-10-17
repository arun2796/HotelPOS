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

    // Four KPI cards shrink gracefully (the value is a DownOnly Viewbox) down to about 225 px each; below that they pair up.
    public static int KpiColumnsFor(double width) => width < 900 ? 2 : 4;

    public static int PanelColumnsFor(double width) => width < CompactWidth ? 2 : 3;

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        KpiRow.Columns = KpiColumnsFor(e.NewSize.Width);
        PanelRow.Columns = PanelColumnsFor(e.NewSize.Width);
    }
}
