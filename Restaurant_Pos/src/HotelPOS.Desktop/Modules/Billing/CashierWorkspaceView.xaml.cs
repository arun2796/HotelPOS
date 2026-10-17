using System.Windows;
using System.Windows.Controls;

namespace HotelPOS.Desktop.Modules.Billing;

public partial class CashierWorkspaceView : UserControl
{
    public CashierWorkspaceView()
    {
        InitializeComponent();
    }

    /// <summary>Below this page width the queue narrows to 340 px so the three panes never need a horizontal scroll.</summary>
    public const double NarrowBelowWidth = 1180;

    // Keyboard focus inside the view makes the payment hotkeys and Esc work straight away.
    private void OnLoaded(object sender, RoutedEventArgs e) => Focus();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width > 0 && e.NewSize.Width < NarrowBelowWidth;
        QueueColumn.Width = new GridLength(narrow ? 340 : 400);
        BillColumn.MinWidth = narrow ? 360 : 420;
    }
}
