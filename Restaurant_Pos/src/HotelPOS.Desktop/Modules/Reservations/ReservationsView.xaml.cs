using System.Windows;
using System.Windows.Controls;

namespace HotelPOS.Desktop.Modules.Reservations;

public partial class ReservationsView : UserControl
{
    // Width of the page (with the editor sidebar closed) at which the "New reservation" label still fits the filter bar.
    private const double LabelMinWidth = 1280;

    public ReservationsView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateLabel(e.NewSize.Width);
    }

    private void UpdateLabel(double width)
    {
        NewReservationLabel.Visibility = width >= LabelMinWidth ? Visibility.Visible : Visibility.Collapsed;
    }
}
