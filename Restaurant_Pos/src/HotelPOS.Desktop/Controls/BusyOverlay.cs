using System.Windows;
using System.Windows.Controls;

namespace HotelPOS.Desktop.Controls;

// Wraps content that is being (re)loaded: while IsBusy, a translucent layer with a progress bar and message covers
// it, so nobody acts on stale rows. The layer fades in after a short delay, so quick loads never flicker.
public sealed class BusyOverlay : ContentControl
{
    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(BusyOverlay), new PropertyMetadata(false));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(BusyOverlay), new PropertyMetadata("Loading…"));

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
