using System.Windows;
using System.Windows.Controls;

namespace HotelPOS.Desktop.Modules.Billing;

public partial class CashierWorkspaceView : UserControl
{
    public CashierWorkspaceView()
    {
        InitializeComponent();
    }

    // Keyboard focus inside the view makes the payment hotkeys and Esc work straight away.
    private void OnLoaded(object sender, RoutedEventArgs e) => Focus();
}
