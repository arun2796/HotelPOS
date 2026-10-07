using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HotelPOS.Desktop.Modules.Billing;

public partial class BillDetailView : UserControl
{
    public BillDetailView()
    {
        InitializeComponent();
    }

    // Keyboard focus inside the view makes the F2/F3/F4/F9/Esc shortcuts work straight away.
    private void OnLoaded(object sender, RoutedEventArgs e) => Focus();

    private void OnAmountFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        (DataContext as BillDetailViewModel)?.Payment?.FocusCommand.Execute(nameof(PaymentViewModel.AmountText));

    private void OnTenderedFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        (DataContext as BillDetailViewModel)?.Payment?.FocusCommand.Execute(nameof(PaymentViewModel.TenderedText));
}
